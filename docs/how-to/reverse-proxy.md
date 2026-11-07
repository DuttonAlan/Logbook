# Configure a reverse proxy

Logbook serves plain HTTP on port 8080 in its container and expects something in front of it to handle HTTPS. This guide configures that proxy, either Caddy or nginx, and tells Logbook to trust it. It adds to the setup from [Deploy to production](deploy-production.md).

## What the proxy must do

| Requirement | Why |
|---|---|
| Pass on the original `Host` | `AllowedHosts` checks it, and the sign-in through OpenID Connect builds its callback address from it. |
| Set `X-Forwarded-For` and `X-Forwarded-Proto` | Logbook needs the address of the client for its rate limits and logs, and the protocol to mark its cookie as `Secure`. The proxy must replace values sent by the client, not only add to them, if it is the first hop. |
| Not buffer `/api/v1/logs/stream` | The [live tail](explore-logs.md) would look silent. Logbook sends `X-Accel-Buffering: no`, which nginx obeys. |
| Allow long-lived responses on that path | A live tail stays open. Its heartbeat comes every 15 seconds, so a read timeout above that is enough. |
| Allow request bodies up to [`LogIngestion:MaxRequestBytes`](../reference/configuration.md#server) | 5 MiB by default. A smaller limit rejects batches with `413`. |
| Allow time for `DELETE /api/v1/applications/{id}` | With many Log Events this takes a while. If the proxy gives up, send the request again to finish. |

## Option A: Caddy

Caddy gets its certificate by itself and sets the forwarded headers. Add it to `compose.yaml`:

```yaml
  proxy:
    image: caddy:2-alpine
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy-data:/data
    networks: [proxy]
```

Add `caddy-data:` under `volumes:`, and create `Caddyfile`:

```text
logbook.example.com {
    reverse_proxy logbook:8080
}
```

## Option B: nginx

With your certificate in `./certificates`, add nginx to `compose.yaml`:

```yaml
  proxy:
    image: nginx:stable-alpine
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/conf.d/default.conf:ro
      - ./certificates:/etc/nginx/certificates:ro
    networks: [proxy]
```

Create `nginx.conf`:

```nginx
server {
    listen 80;
    server_name logbook.example.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    server_name logbook.example.com;

    ssl_certificate     /etc/nginx/certificates/fullchain.pem;
    ssl_certificate_key /etc/nginx/certificates/privkey.pem;

    # At least LogIngestion:MaxRequestBytes.
    client_max_body_size 5m;

    location / {
        proxy_pass http://logbook:8080;
        proxy_http_version 1.1;
        proxy_set_header Connection "";

        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Long enough for the deletion of an Application with many Log Events.
        proxy_read_timeout 300s;
    }
}
```

## Tell Logbook about the proxy

Logbook ignores forwarded headers by default, because a client could otherwise claim any address. Turn them on, and name who may send them:

| Setting | When |
|---|---|
| `ReverseProxy__Enabled=true` | Always behind a proxy. |
| `ReverseProxy__KnownNetworks__0=172.30.0.0/24` | The proxy has a changing address, as in Docker: the network it connects from. |
| `ReverseProxy__KnownProxies__0=10.0.0.5` | The proxy has a fixed address. |
| neither of the last two | The proxy runs on the same host as Logbook, without containers. |
| `ReverseProxy__ForwardLimit=2` | Two proxies in a row, e.g. a CDN in front of nginx. All of them must be known. |

A request from any other address keeps its own address and counts as plain HTTP.

## Check it

Sign in, then look at the response of the sign-in in the developer tools of the browser: the cookie `logbook.session` must be marked `Secure`.

Then try a wrong password and read the log of Logbook:

```sh
docker compose logs logbook | grep "sign-in"
```

It must show your own address. If it shows the address of the proxy, the proxy is not trusted: check the known networks. If the cookie is not `Secure`, the same applies, or `X-Forwarded-Proto` is not set.

Finally, open the live tail in the web interface and send a Log Event: it must appear right away.
