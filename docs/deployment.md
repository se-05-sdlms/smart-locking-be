# Production deployment on AWS EC2

Pushes to `main` run CI, publish `ghcr.io/se-05-sdlms/smart-locking-be:<commit-sha>` from the tested commit, then deploy its immutable digest. PRs targeting `main` or `dev`, and pushes to `dev`, run CI only. CI restores, checks formatting, builds and tests Release, and compiles the demo seeder.

## One-time GitHub setup

1. In **Settings → Environments**, create `production` and restrict deployment branches to exactly `main`. The AWS OIDC subject identifies the environment, so this restriction enforces which branch can assume the role.
2. Set the environment secret `PRODUCTION_ENV` to the full production dotenv file. Use UTF-8, LF, and one `KEY=value` per line. Compose reads values literally in raw mode, including quotes and dollar signs. Enter unquoted values unless the quote characters belong to the value. For .NET config use keys such as `ConnectionStrings__DefaultConnection` with the actual key names used by the application. For a local file, upload with `gh secret set PRODUCTION_ENV --env production < production.env` in Bash.
3. In **Settings → Secrets and variables → Actions → Variables**, create these repository variables. The publish job needs access to `EC2_PLATFORM` before the production environment is used.

   | Variable | Example or meaning |
   | --- | --- |
   | `AWS_REGION` | AWS region of the instance and bucket, e.g. `ap-southeast-1` |
   | `AWS_DEPLOY_ROLE_ARN` | ARN of the GitHub OIDC role described below |
   | `EC2_INSTANCE_ID` | Target `i-…` |
   | `EC2_PLATFORM` | `linux/amd64` for x86-64 or `linux/arm64` for Graviton |
   | `DEPLOY_BUCKET` | Private bucket name, without `s3://` |

4. If the GHCR package already exists, grant this repository Actions write access in its package settings so `GITHUB_TOKEN` can push.
5. The repository default branch is `dev` and already contains `.github/workflows/ci.yml` with `workflow_dispatch`. Keep that workflow path and trigger on the default branch. After merging the EC2 changes to `main`, choose `main` in the **Run workflow** branch selector for production. Sync the workflow change back into `dev` during the normal branch sync.

## One-time AWS setup

- Create a private, dedicated S3 bucket in the deployment region. Enable Block Public Access, bucket-owner-enforced ownership, default SSE-S3 encryption, and a bucket policy that denies non-TLS access. Use a new bucket with versioning disabled so a successful delete removes the env object. Set a one-day expiration rule for `smart-locking-be/production/` for canceled runs, and abort incomplete multipart uploads.
- In IAM, add the GitHub provider `https://token.actions.githubusercontent.com` with audience `sts.amazonaws.com`. Create a role with this trust policy, replacing `ACCOUNT_ID`.

```json
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Principal": {
      "Federated": "arn:aws:iam::ACCOUNT_ID:oidc-provider/token.actions.githubusercontent.com"
    },
    "Action": "sts:AssumeRoleWithWebIdentity",
    "Condition": {
      "StringEquals": {
        "token.actions.githubusercontent.com:aud": "sts.amazonaws.com",
        "token.actions.githubusercontent.com:sub": "repo:se-05-sdlms/smart-locking-be:environment:production"
      }
    }
  }]
}
```

- Give that role `ssm:SendCommand` on both `arn:aws:ssm:REGION::document/AWS-RunShellScript` and `arn:aws:ec2:REGION:ACCOUNT_ID:instance/INSTANCE_ID`. Give it `ssm:GetCommandInvocation` on `*` and `s3:PutObject`, `s3:DeleteObject` on `arn:aws:s3:::BUCKET/smart-locking-be/production/*`. Run Command executes as root on the authorized instance, so protect changes to the production workflow and its script through main-branch review.
- Attach an EC2 instance profile with `AmazonSSMManagedInstanceCore` and `s3:GetObject` on the same S3 prefix. Verify that SSM Agent is running and the instance appears **Online** in Systems Manager. The instance needs outbound HTTPS access to Systems Manager, S3, and GHCR. Install AWS CLI, `curl`, and Docker Compose v2.30 or newer for `env_file.format: raw`.
- Ensure `/opt/smart-locking-be` is controlled by root and `.env` is mode `0600`. Run Command replaces `.env` and manages `compose.yaml`. Reserve host port 8080 and Docker subnet `172.30.250.0/24` for this Compose project. The named volume `smart-locking-be_uploads` preserves local uploads across restarts. Keep any existing uploads backed up and copy them into this volume before going live if using local storage.
- For a private GHCR package, create a classic PAT with `read:packages` for an account allowed to read the package. Authorize SSO if the organization requires it. Over SSH, authenticate Docker as root, the same user that runs SSM. Use a credential helper if available, or protect root's Docker config. Skip login for a public package.

```bash
read -rp 'GitHub username: ' ghcr_user
read -rsp 'GHCR read token: ' ghcr_token
printf '\n'
printf '%s' "$ghcr_token" | sudo -H docker login ghcr.io -u "$ghcr_user" --password-stdin
unset ghcr_token
```

## Nginx and HTTPS

Let Nginx terminate TLS and redirect public HTTP to HTTPS. In its HTTPS virtual host, proxy to the backend and overwrite the forwarded headers with the client address and scheme. The API trusts forwarded headers from the Compose bridge gateway `172.30.250.1`, so it can generate HTTPS image URLs and use client IPs for rate limiting. The host publishes port 8080 on loopback only. Keep it closed in the EC2 security group. For SignalR, define the following map in Nginx's `http` context and use the location in the HTTPS server block.

```nginx
map $http_upgrade $connection_upgrade {
    default upgrade;
    '' close;
}

location / {
    proxy_pass http://127.0.0.1:8080;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $connection_upgrade;
    client_max_body_size 8M;
}
```

After `sudo nginx -t` and reload, check `https://<api-domain>/api/sample/welcome` to verify the public TLS/Nginx path. Update the frontend repository's `VITE_API_BASE_URL` to the new HTTPS API URL and redeploy the frontend. Review the backend's production CORS and public URL configuration for the new domain.

## Update env and operate deployments

- To change env only, replace `PRODUCTION_ENV` and select **Actions → CI → Run workflow → main**. Start a fresh dispatch so it uses the current secret. This requires a healthy release previously recorded by the deploy script and a running container. It verifies the running image ID matches the saved Compose image, then recreates the container with the same local image and new env.
- Each deploy checks `GET /api/sample/welcome` from EC2 and requires HTTP 200. This endpoint queries PostgreSQL. A failed candidate restores the last healthy Compose file and env from the root-only `.last-good.tar`, recreates that image from the local cache, and checks it again. A first failed release removes its candidate container and restores the original files. The GitHub job reports failure after rollback. Use its SSM command ID to see the failed phase and rollback status. Inspect detailed application logs on the host and redact them before sharing.
- The runner uploads the env as an SSE-S3 object with a key unique to the run attempt and deletes it after the command finishes. Keep the local image used by `.last-good.tar` available until the next healthy release. Avoid pruning images during a deploy.
- Run database migrations, seed operations, and production-data changes separately. Apply any required compatible schema change before deploying a version that depends on it.
