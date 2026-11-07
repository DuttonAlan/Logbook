#!/usr/bin/env bash
# Starts the stack from docker-compose.yml, as a new user would, and sends and finds a Log Event through it.
# Uses its own project name and generated secrets, so an existing local stack and its data are not touched.
set -euo pipefail

export COMPOSE_PROJECT_NAME=logbook-smoke-test
export POSTGRES_PASSWORD="$(openssl rand -hex 16)"
export LOGBOOK_ADMIN_PASSWORD="$(openssl rand -hex 32)"

url=http://localhost:5080
cookies="$(mktemp)"

cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then
    docker compose logs --no-color --tail 50 || true
  fi
  rm -f "$cookies"
  docker compose down --volumes --timeout 40
  exit "$status"
}
trap cleanup EXIT

docker compose up --build --detach

for _ in $(seq 1 60); do
  if [ "$(curl --silent --output /dev/null --write-out '%{http_code}' "$url/health")" = 200 ]; then
    break
  fi
  sleep 2
done
curl --silent --fail "$url/health" | jq --exit-status '.status == "Healthy"'

# Everything but the health check needs a session.
test "$(curl --silent --output /dev/null --write-out '%{http_code}' "$url/api/v1/applications")" = 401

curl --silent --fail --cookie-jar "$cookies" -X POST "$url/api/v1/auth/sign-in" \
  -H 'Content-Type: application/json' \
  -d "{\"userName\":\"admin\",\"password\":\"$LOGBOOK_ADMIN_PASSWORD\"}"

curl --silent --fail --cookie "$cookies" "$url/health/details" | jq --exit-status '.status == "Healthy"'

application="$(curl --silent --fail --cookie "$cookies" -X POST "$url/api/v1/applications" \
  -H 'X-Requested-With: curl' \
  -H 'Content-Type: application/json' \
  -d '{"name":"Smoke Test"}')"
application_id="$(jq --raw-output '.id' <<<"$application")"
api_key="$(jq --raw-output '.apiKey.key' <<<"$application")"

curl --silent --fail -X POST "$url/api/v1/logs" \
  -H "X-Logbook-Key: $api_key" \
  -H 'Content-Type: application/json' \
  -d "[{\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"level\":\"Error\",\"message\":\"Smoke test failed on purpose\"}]" \
  | jq --exit-status '.accepted == 1'

search() {
  curl --silent --fail --cookie "$cookies" -G "$url/api/v1/logs" \
    --data-urlencode "applicationId=$application_id" \
    --data-urlencode 'text=purpose'
}

# Log Events are written in the background.
for _ in $(seq 1 30); do
  if search | jq --exit-status '.items | length == 1' >/dev/null; then
    break
  fi
  sleep 1
done
search | jq --exit-status '.items[0].message == "Smoke test failed on purpose"'

# Deleting the Application also deletes its Log Events.
curl --silent --fail --cookie "$cookies" -X DELETE "$url/api/v1/applications/$application_id" -H 'X-Requested-With: curl'
search | jq --exit-status '.items | length == 0'

# The web interface is served by the same container, with the security headers.
curl --silent --fail --dump-header - --output /dev/null "$url/" | grep --ignore-case --quiet '^content-security-policy:'

# Insecure settings must end the process, not just log a message, so that an orchestrator notices.
refused="$(docker run --detach \
  -e 'ConnectionStrings__Database=Host=db;Database=logbook;Username=logbook;Password=unused' \
  -e "SignIn__Administrator__Password=$LOGBOOK_ADMIN_PASSWORD" \
  -e 'AllowedHosts=*' \
  logbook:local)"
for _ in $(seq 1 15); do
  if [ "$(docker inspect --format '{{.State.Running}}' "$refused")" = false ]; then
    break
  fi
  sleep 1
done
test "$(docker inspect --format '{{.State.Running}} {{.State.ExitCode}}' "$refused")" = 'false 1'
docker rm --force "$refused" >/dev/null

echo 'The Compose stack works.'
