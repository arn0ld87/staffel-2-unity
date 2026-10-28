# Wegwerf-Umgebung für den Linux-Player-Test ohne GPU: Xvfb + Mesa (llvmpipe).
# Prüft Funktion (Start, Durchspielen, Speichern), nicht die Bildrate echter Hardware.
FROM ubuntu:latest
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      xvfb xauth libgl1 libglx-mesa0 libgl1-mesa-dri libegl1 libvulkan1 mesa-vulkan-drivers \
      libxcursor1 libxrandr2 libxi6 libxinerama1 libxss1 libgtk-3-0t64 libasound2t64 \
      ffmpeg ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN useradd -m spieler
USER spieler
WORKDIR /home/spieler
