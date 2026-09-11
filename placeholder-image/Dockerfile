# Bootstrap placeholder — push to each service's ECR repo so enable_compute
# can be turned on before any real service image exists. Replace with a real
# CI-built image once one exists; delete this once none of the environments
# reference it anymore.
FROM nginx:alpine
RUN apk add --no-cache curl \
 && echo "OK" > /usr/share/nginx/html/health \
 && sed -i 's/listen       80;/listen       8080;/' /etc/nginx/conf.d/default.conf
EXPOSE 8080
