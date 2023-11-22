FROM portainer/portainer-ee:2.19.3-alpine

RUN apk --no-cache add tzdata && rm -rf /var/cache/apk/*

ENTRYPOINT ["/portainer"]