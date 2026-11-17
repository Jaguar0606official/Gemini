# Gemini Network Assistant — Final 1.0

Windows 10/11 WPF network diagnostic and DNS management utility.

## What it does
- Lists active network adapters, IPv4 addresses, gateways and DNS servers.
- Checks the public IP and approximate exit country via ipify + ipwho.is (the public IP is sent to those services).
- Can monitor public IP every 60 seconds; monitoring is opt-in and can be stopped.
- Tests DNS, TCP 443 and HTTPS responses for Gemini, account, Antigravity and Google API hostnames.
- Allows manual DNS changes with a persistent backup of the original DHCP/static mode and DNS addresses.
- Restores DHCP DNS mode or original static DNS settings from `%LOCALAPPDATA%\GeminiNetworkAssistant\dns-backup.json`.

## Important limitations
This tool does not create a VPN/proxy tunnel, change account country, or bypass Google's regional eligibility rules. DNS changes do not change public IP. Public-IP geolocation is approximate. Successful HTTPS responses only demonstrate network reachability, not product access.

## Build on GitHub Actions
Upload the contents of this folder to the root of your repository (the `.csproj` and `.github` folder must be at repository root). Then open Actions → Build Windows app → Run workflow. Download artifact `GeminiNetworkAssistant-win-x64`.

For DNS changes, run the app as Administrator. Diagnostics and public-IP checks do not require Administrator rights. DNS backup is saved before the first change per interface and reused on later runs; if you intentionally want a new baseline after changing network configuration, remove the JSON backup file manually.
