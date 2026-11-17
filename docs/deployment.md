# Production deployment on AWS EC2

Pushes to `main` run CI, publish `ghcr.io/se-05-sdlms/smart-locking-be:<commit-sha>` from the tested commit, then deploy its immutable digest. PRs targeting `main` or `dev`, and pushes to `dev`, run CI only. CI restores, checks formatting, builds and tests Release, and compiles the demo seeder.

## One-time GitHub setup

1. In **Settings → Environments**, create `production` and restrict deployment branches to `main`. Add environment secret `EC2_SSH_PRIVATE_KEY` with the full dedicated SSH private key, including the header, footer, and line breaks. Keep the private key off EC2.
2. Under **Secrets and variables → Actions → Variables**, set the repository variables below.

   | Variable | Value |
   | --- | --- |
   | `EC2_PLATFORM` | `linux/amd64` for `uname -m` = `x86_64`, or `linux/arm64` for `aarch64` |
   | `EC2_HOST` | EC2 DNS name or IPv4 address, accessible from the deploy runner on TCP 22 |
   | `EC2_SSH_KNOWN_HOSTS` | One verified OpenSSH known_hosts line for that exact host |

3. If the GHCR package already exists, give this repository Actions write access in its package settings so `GITHUB_TOKEN` can push.
4. The default branch `dev` already contains `.github/workflows/ci.yml` with `workflow_dispatch`. Keep that path and trigger on `dev`. After merging the changes to `main`, select `main` in **Run workflow** for an env reload.

The SSH version needs the three variables and private key above. Configuration from earlier OIDC/S3 deployment drafts is unused.

## One-time EC2 setup

- Install `curl` and Docker Compose v2.30 or newer for `env_file.format: raw`. Reserve port 8080 on loopback and subnet `172.30.250.0/24` for this Compose project. The named volume `smart-locking-be_uploads` preserves local uploads across restarts. Back up and migrate existing local uploads into the volume before going live if applicable.
- The GitHub runner must reach TCP 22 on EC2. If the security group only allows your own IP, the default `ubuntu-latest` runner will fail to connect. For a restricted security group, use a deploy runner with a known source IP or private network route and update the deploy job's `runs-on` accordingly. [GitHub-hosted runner networking](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)
- From your trusted admin SSH session, run `sudo cat /etc/ssh/ssh_host_ed25519_key.pub`. For `EC2_SSH_KNOWN_HOSTS`, use a line of the form `<EC2_HOST> ssh-ed25519 <public-key-base64>`. The value starts with the exact GitHub `EC2_HOST` and uses the first two fields of the server public key. The workflow verifies this key before authenticating.

From your own computer, generate a dedicated deploy key with no passphrase for the unattended job. Use your existing admin access to copy the reviewed script from the repo root to EC2.

```bash
ssh-keygen -t ed25519 -f ~/.ssh/smart-locking-actions -C github-actions-smart-locking
EC2_HOST=your-ec2-hostname
scp .github/scripts/deploy-ec2.sh "ubuntu@$EC2_HOST:~/smart-locking-deploy"
```

Use the same SSH identity file and options as your working admin connection if SCP needs them.

On EC2 as the admin user, run the following. Preserve the `.env` you already created. Edit it with `sudoedit /opt/smart-locking-be/.env` and paste your production env contents. Use UTF-8, LF and literal `KEY=value` lines. Quotes and dollar signs are literal in raw Compose format.

```bash
sudo install -d -o root -g root -m 0750 /opt/smart-locking-be
sudo touch /opt/smart-locking-be/.env
sudo chown root:root /opt/smart-locking-be/.env
sudo chmod 0600 /opt/smart-locking-be/.env
sudo install -o root -g root -m 0755 ~/smart-locking-deploy /usr/local/sbin/smart-locking-deploy
sudo adduser --disabled-password --gecos '' deploy
sudo install -d -o deploy -g deploy -m 0700 /home/deploy/.ssh
sudo visudo -f /etc/sudoers.d/smart-locking-deploy
```

If the `deploy` user already exists, reuse it only if it is dedicated to this deployment and has no broader sudo or Docker group access. In the sudoers file, add this single line:

```sudoers
deploy ALL=(root) NOPASSWD: /usr/local/sbin/smart-locking-deploy
```

In `/home/deploy/.ssh/authorized_keys`, add the following as one line, replacing the placeholder with the entire `smart-locking-actions.pub` line. Keep the backslashes before the inner quotes. Set the file owner to `deploy:deploy` and mode `0600`. Use the matching private key file for the GitHub secret.

```text
restrict,command="/usr/bin/sudo -n -H /usr/local/sbin/smart-locking-deploy \"$SSH_ORIGINAL_COMMAND\"" <SSH_PUBLIC_KEY_LINE>
```

The forced command restricts that key to the installed deploy script. The script accepts only `reload` or a digest from this repository's GHCR image. When the reviewed deploy script changes in Git, install the updated script on EC2 through the same admin procedure before using it.

For a private GHCR package, create a classic PAT with `read:packages` for an account that can read the package. Authorize SSO if the organization requires it. Authenticate Docker as root, the user running Compose. Skip login for a public package.

```bash
read -rp 'GitHub username: ' ghcr_user
read -rsp 'GHCR read token: ' ghcr_token
printf '\n'
printf '%s' "$ghcr_token" | sudo -H docker login ghcr.io -u "$ghcr_user" --password-stdin
unset ghcr_token
```

From an allowed network, preflight the restricted key with `ssh -i ~/.ssh/smart-locking-actions -o IdentitiesOnly=yes "deploy@$EC2_HOST" invalid`. The expected response is `Expected reload or a digest for the smart-locking-be GHCR image.` and a nonzero exit. This reaches the real deploy entry point without changing the container.

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

- To change env only, edit `/opt/smart-locking-be/.env` over your admin SSH session using `sudoedit`. Then choose **Actions → CI → Run workflow → main**. The deploy job verifies the running image ID and recreates that image locally. This requires an earlier healthy deployment and a running container. Finish edits before starting a deploy.
- Each deploy requires HTTP 200 from `GET /api/sample/welcome` on EC2, which checks PostgreSQL. A failed candidate restores the last healthy Compose file and env from the root-only `.last-good.tar` and rechecks the previous release. On a failed first release, the script removes the candidate and restores the starting files. The workflow reports failure even when rollback succeeds. Keep the last healthy image in Docker's local cache.
- Run database migrations, seed operations, and production-data changes separately. Apply compatible schema changes before deploying a version that depends on them.
