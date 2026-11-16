#!/bin/bash
if [ "$EUID" -ne 0 ]; then
  echo "Vui long chay bang quyen root: sudo ./install-agent.sh"
  exit 1
fi

INSTALL_DIR="/opt/netops-agent"
mkdir -p $INSTALL_DIR

if [ -f "./NetOps.Agent" ]; then
    cp ./NetOps.Agent $INSTALL_DIR/
    chmod +x $INSTALL_DIR/NetOps.Agent
else
    echo "LOI: Khong tim thay file NetOps.Agent cung thu muc."
    exit 1
fi

cat <<EOT > /etc/systemd/system/netops-agent.service
[Unit]
Description=NetOps Manager - Background Agent Service
After=network.target

[Service]
Type=notify
ExecStart=$INSTALL_DIR/NetOps.Agent
WorkingDirectory=$INSTALL_DIR
Restart=always
RestartSec=10
SyslogIdentifier=netops-agent

[Install]
WantedBy=multi-user.target
EOT

systemctl daemon-reload
systemctl enable netops-agent.service
systemctl start netops-agent.service
systemctl status netops-agent.service --no-pager