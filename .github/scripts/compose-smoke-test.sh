#!/usr/bin/env bash
# Starts the stack from docker-compose.yml, as a new user would, and sends and finds a Log Event through it.
# Uses its own project name and generated secrets, so an existing local stack and its data are not touched.
set -euo pipefail

export COMPOSE_PROJECT_NAME=logbook-smoke-test
export POSTGRES_PASSWORD="$(openssl rand -hex 16)"
export LOGBOOK_ADMIN_KEY="$(openssl rand -hex 32)"

url=http://localhost:5080

cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then
    docker compose logs --no-color --tail 50 || true
  fi
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

application="$(curl --silent --fail -X POST "$url/api/v1/applications" \
  -H "Authorization: Bearer $LOGBOOK_ADMIN_KEY" \
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
  curl --silent --fail -G "$url/api/v1/logs" \
    -H "Authorization: Bearer $LOGBOOK_ADMIN_KEY" \
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
curl --silent --fail -X DELETE "$url/api/v1/applications/$application_id" -H "Authorization: Bearer $LOGBOOK_ADMIN_KEY"
search | jq --exit-status '.items | length == 0'

# The web interface is served by the same container.
curl --silent --fail --output /dev/null "$url/"

echo 'The Compose stack works.'
