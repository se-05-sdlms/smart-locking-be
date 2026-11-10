# Production deployment

Pull requests into `dev` and `main`, and pushes to either branch, run restore, format verification, Release build, and tests. CI also compiles the demo seeder without running it.

After those checks pass on a push to `main`, GitHub Actions calls Render's deploy hook with `ref=${GITHUB_SHA}`. Render builds the exact tested commit using the root Dockerfile. Manual workflow dispatch on `main` uses the same checks and deploy job; dispatch on `dev` runs checks only.

The Render service `smart-locking-be` tracks `main`, with native auto-deploy **Off**. Keep it off so production deployment is controlled by the passing GitHub Actions job. Store the private hook URL only in the repository secret `RENDER_DEPLOY_HOOK_URL`. Database credentials and integration secrets remain in Render environment variables.

API: https://smart-locking-be.onrender.com

Frontend: https://smart-locking-fe.pages.dev

The frontend repository's Cloudflare Pages workflow tests and builds every PR/push to `dev` or `main`, then deploys production only from `main`. Its `VITE_API_BASE_URL` repository variable must be a public HTTPS URL; production/PR checks reject a missing or local API URL.

Database migrations and demo seeding are explicit operations, not automatic deployment steps. The production database already has the 25 current migrations; future schema changes must be reviewed and migrated before deploying code that needs them. Deployment never resets or reseeds production data.
