# Bootstrap placeholder — push to each service's ECR repo so enable_compute
# can be turned on before any real service image exists. Replace with a real
# CI-built image once one exists; delete this once none of the environments
# reference it anymore.
FROM nginx:alpine
# Self-signed cert so this placeholder also passes the new HTTPS healthcheck.
RUN apk add --no-cache curl openssl \
 && echo "OK" > /usr/share/nginx/html/health \
 && mkdir -p /etc/nginx/certs \
 && openssl req -x509 -newkey rsa:2048 -nodes \
      -keyout /etc/nginx/certs/server.key \
      -out /etc/nginx/certs/server.crt \
      -days 825 \
      -subj "/CN=placeholder.internal" \
 && sed -i 's/listen       80;/listen       8080;/' /etc/nginx/conf.d/default.conf \
 && cat >> /etc/nginx/conf.d/default.conf <<'EOF'
server {
    listen       8443 ssl;
    server_name  localhost;

    ssl_certificate     /etc/nginx/certs/server.crt;
    ssl_certificate_key /etc/nginx/certs/server.key;

    location / {
        root   /usr/share/nginx/html;
        index  index.html index.htm;
    }
}
EOF
EXPOSE 8080
EXPOSE 8443
