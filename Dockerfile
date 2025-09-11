FROM portainer/portainer-ee:2.33.1-alpine

RUN apk add --no-cache tzdata nginx supervisor && rm -rf /var/cache/apk/*

COPY nginx.conf /etc/nginx/nginx.conf
COPY supervisord.conf /etc/supervisord.conf

EXPOSE 80

ENTRYPOINT ["/usr/bin/supervisord", "-c", "/etc/supervisord.conf"]