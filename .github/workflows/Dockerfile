# syntax=docker/dockerfile:1
#=====================================
#
     FROM ubuntu:22.04 AS builder
#
#=====================================
ARG DEBIAN_FRONTEND=noninteractive
ARG UNITY_VERSION
ARG PROJECT

ENV INSTALL_ROOT=/opt/unity \
    UNITY_HOME=/opt/unity \
    UNITY_VERSION=${UNITY_VERSION} \
    PROJECT=${PROJECT} \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    UNITY_THISISABUILDMACHINE=1 \
    PATH="/usr/local/bin:${PATH}"

RUN \
--mount=type=cache,target=/var/cache/apt,id=apt-cache-builder,sharing=locked \
--mount=type=cache,target=/var/lib/apt/lists,id=apt-lists-builder,sharing=locked \
--mount=type=bind,source=.github/workflows/test-manifest.json,target=/tmp/manifest.json,readonly \
\
#========================
<<'BUILDER_SETUP_SCRIPT'
#========================

set -eux

# debian nodoc
echo 'path-exclude /usr/share/man/*'               >  /etc/dpkg/dpkg.cfg.d/01_nodoc
echo 'path-exclude /usr/share/doc/*'               >> /etc/dpkg/dpkg.cfg.d/01_nodoc
echo 'path-exclude /usr/share/info/*'              >> /etc/dpkg/dpkg.cfg.d/01_nodoc
echo 'path-exclude /usr/share/locale/*'            >> /etc/dpkg/dpkg.cfg.d/01_nodoc
echo 'path-include /usr/share/locale/locale.alias' >> /etc/dpkg/dpkg.cfg.d/01_nodoc

# core runtime unity editor deps
apt-get update -o Acquire::Retries=10 -qq
apt-get install -y --no-install-recommends \
  tini binutils ca-certificates xz-utils axel xvfb libgtk-3-0 libglu1-mesa \
  libxrandr2 libxinerama1 libxcursor1 libxi6 libxss1 libnss3 libgbm1 fonts-dejavu-core
if ! dpkg -s libasound2 >/dev/null 2>&1 && ! dpkg -s libasound2t64 >/dev/null 2>&1; then
  apt-get install -y --no-install-recommends libasound2t64 || apt-get install -y --no-install-recommends libasound2
fi
if ! dpkg -s libcups2 >/dev/null 2>&1 && ! dpkg -s libcups2t64 >/dev/null 2>&1; then
  apt-get install -y --no-install-recommends libcups2t64 || apt-get install -y --no-install-recommends libcups2
fi

# clear apt lists
rm -rf /var/lib/apt/lists/*

# install libssl1.1 for older unity versions
# https://discussions.unity.com/t/workaround-for-libssl-issue-on-ubuntu-22-04/879165
# https://stackoverflow.com/questions/73378850/how-can-i-install-unity-hub-on-ubuntu-22-04
axel -q -o libssl1.1_1.1.1f-1ubuntu2.24_amd64.deb http://archive.ubuntu.com/ubuntu/pool/main/o/openssl/libssl1.1_1.1.1f-1ubuntu2.24_amd64.deb
dpkg -i libssl1.1_1.1.1f-1ubuntu2.24_amd64.deb
rm -f libssl1.1_1.1.1f-1ubuntu2.24_amd64.deb

DEST="${INSTALL_ROOT}/${UNITY_VERSION}"
mkdir -p "$DEST"
TARBALL="/tmp/unity.tar.xz"
BASE="${UNITY_VERSION%f*}"
TMPHTML="/tmp/unity_whats_new.html"
axel -q -o "$TMPHTML" "https://unity.com/releases/editor/whats-new/${UNITY_VERSION}"
URL="$( \
  tr '\n' ' ' < "$TMPHTML" \
  | grep -oE "https://download\.unity3d\.com[^\"']+Unity-${UNITY_VERSION}\.tar\.xz" \
  | head -n1 \
)"
rm -f "$TMPHTML"
test -n "$URL"

# extract revision
REV="$(printf '%s' "$URL" | sed -n 's#.*/download_unity/\([^/]\+\)/.*#\1#p')"
test -n "$REV"
echo "Downloading: $URL (revision: $REV)"

# https://www.tecmint.com/axel-commandline-download-accelerator-for-linux/
axel -q -n 16 -o "$TARBALL" "$URL"
xz -T0 -dc "$TARBALL" | tar -x --no-same-owner --no-same-permissions --delay-directory-restore --no-xattrs --no-acls --no-selinux -C "$DEST"
rm -f "$TARBALL"

# strip editor
rm -rf "$DEST/Editor/libumbraoptimizer64.*" || true
rm -rf "$DEST/Editor/libembree.*" || true
rm -rf "$DEST/Editor/libRadeonRays.*" || true
rm -rf "$DEST/Editor/libRL.*" || true
rm -rf "$DEST/Editor/Unity_s.debug" || true
rm -rf "$DEST/Editor/Data/il2cpp" || true
rm -rf "$DEST/Editor/Data/Tools/usymtool" || true
rm -rf "$DEST/Editor/Data/Tools/macosx" || true
rm -rf "$DEST/Editor/Data/Tools/libembree.*" || true
rm -rf "$DEST/Editor/Data/Tools/libdxcompiler.*" || true
rm -rf "$DEST/Editor/Data/Tools/libUnityDenoisingPlugin.so" || true
rm -rf "$DEST/Editor/Data/Tools/libRadeonRays.*" || true
rm -rf "$DEST/Editor/Data/Tools/libRL.*" || true
rm -rf "$DEST/Editor/Data/Tools/libOpenImageDenoise.*" || true
rm -rf "$DEST/Editor/Data/Tools/astcenc-sse42.so" || true
rm -rf "$DEST/Editor/Data/Tools/astcenc-sse2.so" || true
rm -rf "$DEST/Editor/Data/Tools/astcenc-avx2.so" || true
rm -rf "$DEST/Editor/Data/Tools/VersionControl" || true
rm -rf "$DEST/Editor/Data/Tools/PVRTexTool" || true
rm -rf "$DEST/Editor/Data/Tools/LightBaker" || true
rm -rf "$DEST/Editor/Data/Tools/*_s.debug" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/ProjectTemplates" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/PackageTemplates" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/Editor/*.tgz" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/Diagnostics" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.visualeffectgraph" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.shadergraph" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.shaderanalysis" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.rendering.light-transport" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.rendering.denoising" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.universal-config" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.universal" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition-config" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.high-definition" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.render-pipelines.core" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.path-tracing" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.multiplayer.center" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.multiplayer.center" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.2d.tilemap" || true
rm -rf "$DEST/Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.2d.sprite" || true
rm -rf "$DEST/Editor/Data/Resources/OpenRL" || true
rm -rf "$DEST/Editor/Data/Resources/GI" || true
rm -rf "$DEST/Editor/Data/PlaybackEngines" || true
rm -rf "$DEST/Editor/Data/MonoEmbedRuntime" || true
rm -rf "$DEST/Editor/Data/MonoBleedingEdge/etc" || true
rm -rf "$DEST/Editor/Data/MonoBleedingEdge/bin" || true
rm -rf "$DEST/Editor/BugReporter" || true
rm -rf "$DEST/Editor/*_s.debug" || true
find "$DEST/Editor/Data/Resources/PackageManager/Editor/" ! -name 'manifest.json' -type f -exec rm -f {} + || true
find "$DEST" -type f \( -name '*.mdb' -o -name '*.xml' -o -name '*.dbg' \) -delete || true
find "$DEST" -type f \( -name '*.a' -o -name '*.la' -o -name '*.pdb' \) -delete || true
find "$DEST" -type f -name '*.so' -exec strip --strip-unneeded {} + 2>/dev/null || true
strip --strip-unneeded "$DEST/Editor/Unity" 2>/dev/null || true
test -x "${DEST}/Editor/Unity"

# copy manifest.json
mkdir -p "${PROJECT}/Packages" "${PROJECT}/ProjectSettings" "${PROJECT}/Assets"
cp /tmp/manifest.json "${PROJECT}/Packages/manifest.json"

# create project.json
printf '%s\n' \
  "m_EditorVersion: ${UNITY_VERSION}" \
  "m_EditorVersionWithRevision: ${UNITY_VERSION} (${REV})" \
  > "${PROJECT}/ProjectSettings/ProjectVersion.txt"

apt-get purge --autoremove -y xz-utils binutils axel

install -d /usr/local/bin

printf '%s\n' '#!/usr/bin/env bash' \
  'set -euo pipefail' \
  'Xvfb :99 -screen 0 1024x768x24 > /dev/null 2>&1 &' \
  'xvfb_pid=$!' \
  "trap 'kill \$xvfb_pid 2>/dev/null || true; wait \$xvfb_pid 2>/dev/null || true' EXIT" \
  'export DISPLAY=:99' \
  'exec "$@"' \
  > /usr/local/bin/with-xvfb && chmod +x /usr/local/bin/with-xvfb

printf '%s\n' '#!/usr/bin/env bash' \
  'set -euo pipefail' \
  'EDITOR_PATH="${INSTALL_ROOT}/${UNITY_VERSION}/Editor/Unity"' \
  ': "${HOME:=/root}"' \
  ': "${XDG_CACHE_HOME:=$HOME/.cache}"' \
  ': "${XDG_CONFIG_HOME:=$HOME/.config}"' \
  'mkdir -p "${XDG_CACHE_HOME}/unity3d" "${XDG_CONFIG_HOME}/unity3d" "/root/.local/share/unity3d"' \
  'ulimit -n 65535' \
  'ulimit -u 16384' \
  'SERIAL="${UNITY_SERIAL:-}"' \
  'USERN="${UNITY_USER:-}"' \
  'PASSW="${UNITY_PASSWORD:-}"' \
  'if [ -f /run/secrets/unity_serial ]; then SERIAL="$(cat /run/secrets/unity_serial)"; fi' \
  'if [ -f /run/secrets/unity_user ]; then USERN="$(cat /run/secrets/unity_user)"; fi' \
  'if [ -f /run/secrets/unity_password ]; then PASSW="$(cat /run/secrets/unity_password)"; fi' \
  ': "${SERIAL:?UNITY serial is required}"' \
  ': "${USERN:?UNITY username is required}"' \
  ': "${PASSW:?UNITY password is required}"' \
  'exec with-xvfb "$EDITOR_PATH"  -serial "${SERIAL}" -username "${USERN}" -password "${PASSW}" -silent-crashes -batchmode -nographics -logFile - "$@"' \
  > /usr/local/bin/unity-editor && chmod +x /usr/local/bin/unity-editor

rm -rf "/root/.cache/unity3d" "/root/.config/unity3d" "/root/.local/share/unity3d" "${PROJECT}/Temp" || true

#=====================
#   (END OF THE)
BUILDER_SETUP_SCRIPT
#=====================



#=====================================
#
          FROM ubuntu:22.04
#
#=====================================
ARG DEBIAN_FRONTEND=noninteractive
ARG UNITY_VERSION
ARG PROJECT

ENV INSTALL_ROOT=/opt/unity \
    UNITY_HOME=/opt/unity \
    UNITY_VERSION=${UNITY_VERSION} \
    PROJECT=${PROJECT} \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    UNITY_THISISABUILDMACHINE=1 \
    PATH="/usr/local/bin:${PATH}"

RUN \
--mount=type=cache,target=/var/cache/apt,id=apt-cache-runtime,sharing=locked \
--mount=type=cache,target=/var/lib/apt/lists,id=apt-lists-runtime,sharing=locked \
\
#==========================
<<'CONTAINER_SETUP_SCRIPT'
#==========================

set -eux

# debian nodoc
echo 'path-exclude /usr/share/man/*'               >  /etc/dpkg/dpkg.cfg.d/01_nodoc
echo 'path-exclude /usr/share/doc/*'               >> /etc/dpkg/dpkg.cfg.d/01_nodoc
echo 'path-exclude /usr/share/info/*'              >> /etc/dpkg/dpkg.cfg.d/01_nodoc
echo 'path-exclude /usr/share/locale/*'            >> /etc/dpkg/dpkg.cfg.d/01_nodoc
echo 'path-include /usr/share/locale/locale.alias' >> /etc/dpkg/dpkg.cfg.d/01_nodoc

apt-get update -o Acquire::Retries=10 -qq
apt-get install -y --no-install-recommends \
    tini ca-certificates xvfb libgtk-3-0 libglu1-mesa libxrandr2 \
    libxinerama1 libxcursor1 libxi6 libxss1 libnss3 libgbm1 fonts-dejavu-core
if ! dpkg -s libasound2 >/dev/null 2>&1 && ! dpkg -s libasound2t64 >/dev/null 2>&1; then
  apt-get install -y --no-install-recommends libasound2t64 || apt-get install -y --no-install-recommends libasound2
fi
if ! dpkg -s libcups2 >/dev/null 2>&1 && ! dpkg -s libcups2t64 >/dev/null 2>&1; then
  apt-get install -y --no-install-recommends libcups2t64 || apt-get install -y --no-install-recommends libcups2
fi
rm -rf /var/lib/apt/lists/*

#======================
#    (END OF THE)
CONTAINER_SETUP_SCRIPT
#======================

COPY --link --from=builder /usr/lib/x86_64-linux-gnu/libssl.so.1.1 /usr/lib/x86_64-linux-gnu/libcrypto.so.1.1 /usr/lib/x86_64-linux-gnu/
COPY --link --from=builder /opt/unity/${UNITY_VERSION} /opt/unity/${UNITY_VERSION}
COPY --link --from=builder /usr/local/bin/with-xvfb /usr/local/bin/with-xvfb
COPY --link --from=builder /usr/local/bin/unity-editor /usr/local/bin/unity-editor
COPY --link --from=builder ${PROJECT} ${PROJECT}

ENTRYPOINT ["/usr/bin/tini","--"]
CMD ["bash","-lc","unity-editor -h || true"]