ARG BUILD_FROM
FROM ${BUILD_FROM}

RUN apk add --no-cache curl

ARG BUILD_ARCH
RUN if [ "${BUILD_ARCH}" = "aarch64" ]; then ARCH="arm64"; fi && \
    if [ "${BUILD_ARCH}" = "armhf" ]; then ARCH="arm"; fi && \
    if [ "${BUILD_ARCH}" = "armv7" ]; then ARCH="arm"; fi && \
    if [ "${BUILD_ARCH}" = "amd64" ]; then ARCH="amd64"; fi && \
    curl -L -s "https://github.com/portainer/portainer/releases/download/2.13.1/portainer-2.13.1-linux-${ARCH}.tar.gz" | tar zxvf - -C /opt/

COPY /init.sh /

RUN chmod +x /init.sh

ENTRYPOINT ["sh", "/init.sh"]
