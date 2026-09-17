FROM nginx:alpine

RUN apk add --no-cache openssl ca-certificates

COPY nginx.conf /etc/nginx/nginx.conf
COPY default.conf /etc/nginx/conf.d/default.conf
COPY entrypoint.sh /docker-entrypoint.d/99-generate-certs.sh

RUN chmod +x /docker-entrypoint.d/99-generate-certs.sh

EXPOSE 80 443

CMD ["nginx", "-g", "daemon off;"]
