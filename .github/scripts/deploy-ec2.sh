#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

if [[ $# != 1 || ( $1 != reload && ! $1 =~ ^ghcr\.io/se-05-sdlms/smart-locking-be@sha256:[a-f0-9]{64}$ ) ]]; then
  echo 'Expected reload or a digest for the smart-locking-be GHCR image.' >&2
  exit 1
fi

exec 3>&1 >/dev/null 2>&1

cd /opt/smart-locking-be || { echo 'Deployment directory is unavailable.' >&3; exit 1; }
exec 9>.deploy.lock
flock -w 30 9 || { echo 'Another deployment owns the EC2 lock.' >&3; exit 1; }

stage=$(mktemp -d .deploy.XXXXXXXX)
phase='prepare deployment'
changed=0
started=0
compose() {
  docker compose --env-file /dev/null --project-name smart-locking-be -f "$1/compose.yaml" "${@:2}"
}
up() {
  compose . up -d --no-build --pull never --force-recreate api
}
healthy() {
  local code
  for ((attempt=0; attempt<24; attempt++)); do
    if code=$(curl --silent --max-time 3 --output /dev/null --write-out '%{http_code}' http://127.0.0.1:8080/api/sample/welcome) && [[ $code == 200 ]]; then
      return 0
    fi
    sleep 2
  done
  return 1
}
restore_file() {
  if [[ -f $stage/original/$1 ]]; then
    mv -f -- "$stage/original/$1" "$1"
  else
    rm -f -- "$1"
  fi
}
finish() {
  local result=$?
  trap - EXIT
  if (( result != 0 )); then
    echo "Deployment failed while attempting to $phase." >&3
    if (( changed )); then
      if [[ -f .last-good.tar ]]; then
        if tar -xf .last-good.tar && up && healthy; then
          echo 'Restored the last healthy image and env.' >&3
        else
          echo 'Rollback failed. Check EC2 locally.' >&3
        fi
      else
        local cleanup_ok=1
        if (( started )) && ! compose . rm -sf api; then cleanup_ok=0; fi
        if restore_file .env && restore_file compose.yaml && (( cleanup_ok )); then
          echo 'Removed the failed first release and restored the original files.' >&3
        else
          echo 'First-release cleanup failed. Check EC2 locally.' >&3
        fi
      fi
    fi
  fi
  rm -rf -- "$stage"
  exit "$result"
}
trap finish EXIT
trap 'exit 1' HUP INT TERM

phase='prepare candidate image'
if [[ $1 != reload ]]; then
  image=$1
  docker pull "$image"
  cat > "$stage/compose.yaml" <<EOF
services:
  api:
    image: $image
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"
    env_file:
      - path: .env
        format: raw
    environment:
      ReverseProxy__Address: 172.30.250.1
    volumes:
      - uploads:/app/wwwroot/uploads
networks:
  default:
    ipam:
      config:
        - subnet: 172.30.250.0/24
          gateway: 172.30.250.1
volumes:
  uploads:
EOF
else
  phase='identify the currently running release'
  [[ -f .last-good.tar ]]
  container=$(compose . ps --status running --quiet api)
  [[ -n $container ]]
  image=$(compose . config --images)
  [[ $(docker inspect --format '{{.Image}}' "$container") == "$(docker image inspect --format '{{.Id}}' "$image")" ]]
  cp -- compose.yaml "$stage/compose.yaml"
fi

phase='validate the EC2 env file'
[[ -f .env && -s .env ]]
cp -- .env "$stage/.env"
compose "$stage" config --quiet

phase='install release files'
mkdir -- "$stage/original"
for file in .env compose.yaml; do
  if [[ -f $file ]]; then cp -- "$file" "$stage/original/$file"; fi
done
changed=1
mv -f -- "$stage/.env" .env
mv -f -- "$stage/compose.yaml" compose.yaml

phase='recreate and check the API'
started=1
up
healthy

phase='record the healthy release'
tar -cf "$stage/last-good.tar" .env compose.yaml
mv -f -- "$stage/last-good.tar" .last-good.tar
echo 'Deployment passed the API and PostgreSQL check.' >&3
