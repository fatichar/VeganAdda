# Existing VPS inspection

Read-only inspection on 2026-10-17 using the owner's supplied SSH endpoint (port 22222, ubuntu account, existing ovh_vps key). No files, containers, DNS records, certificates, firewall rules or services were changed.

- Linux kernel 7.0.0-28-generic; Docker Compose v5.6.0; host Nginx configuration passes `nginx -t`.
- Host Nginx owns ports 80/443. Existing sites: Dinzer, Thoughtflow, Greenfit, analytics, and PAL comments. A default port-80 site also exists.
- Existing local upstreams/listeners: 3000, 3001, 3002, 6000, 8080, 10000, 18080; Dinzer Nginx also references 18081. **5080 was unused** at inspection.
- Running stacks include Dinzer + PostgreSQL 18, Thoughtflow web/API, Remark42, Uptime Kuma, Umami + PostgreSQL 16, and PAL WordPress + MariaDB. None should be replaced or reused for VeganAdda storage.
- Existing Docker ranges: 172.17.0.0/16 through 172.23.0.0/16. **172.30.87.0/24 did not overlap** the inspected networks. Compose reserves this isolated subnet; gateway 172.30.87.1 is the proposed trusted host-proxy source. Confirm the actual observed proxy address during staging before relying on forwarded headers.
- `/etc/nginx/conf.d/dinzer-upgrade.conf` already defines `$connection_upgrade`. The VeganAdda example reuses that map, without adding a duplicate.
- Certificate inventory contains the existing domains, with **no veganadda.com certificate**. `getent ahostsv4 veganadda.com` returned no address on the VPS at inspection. Owner must configure/verify DNS before requesting a certificate.
- Root disk: 38 GB total, 28 GB free. RAM: about 3.7 GiB total and 1.7 GiB available, no swap. Current containers have modest usage but share this machine. Build the release image elsewhere where practical; measure resources during the Pune pilot before adjusting limits or adding swap.

## Proposed additive deployment, not executed

Use a new directory such as `/opt/veganadda`, its own `veganadda` Compose project, dedicated database/media/key volumes, a localhost-only upstream at 5080, and a separate `/etc/nginx/sites-available/veganadda.com` vhost. Preserve every existing vhost, volume and container. Obtain the new certificate through the existing Certbot setup after DNS verification. See the README for sequencing and rollback. Deployment still requires explicit owner authorization.
