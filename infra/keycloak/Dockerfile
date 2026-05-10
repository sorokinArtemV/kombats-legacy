# infra/keycloak/Dockerfile
# Custom Kombats Keycloak image: classic FreeMarker theme `kombats` baked in.
# Build context: repo root (matches pipelines/build-backend.yml convention).
#   docker build -f infra/keycloak/Dockerfile -t kombats-keycloak .

FROM quay.io/keycloak/keycloak:24.0 AS builder
COPY infra/keycloak-themes/kombats /opt/keycloak/themes/kombats
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:24.0
COPY --from=builder /opt/keycloak/ /opt/keycloak/
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
