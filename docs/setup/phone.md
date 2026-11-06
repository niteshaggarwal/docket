# Phone access with Tailscale

The Windows install keeps your task server private: it listens only on your PC (`127.0.0.1`). To reach it from
your phone without opening anything to the internet, use [Tailscale](https://tailscale.com). It's free for personal
use and makes a private network of your own devices.

> [!NOTE]
> Using a [Docker server](docker.md) with a domain? You don't need this page: sign in on the phone with
> `https://your-domain`.

## Set it up

1. **Install Tailscale on the PC** from [tailscale.com/download](https://tailscale.com/download) and sign in
   (tray icon > **Log in**).
2. **Install Tailscale on the phone** (Google Play) and sign in to the **same account**.
3. **Share Docket from the PC.** Open PowerShell and run:

   ```powershell
   tailscale serve --bg 3457
   ```

   This gives your PC an HTTPS address like `https://my-pc.tail1234.ts.net` that only your own devices can open.
   Port 3457 is [Docket web](docket-web.md), so the same address also works in your phone's browser. (Use 3456 to
   share plain Vikunja instead; the Docket apps work with either.)
4. **Install Docket for Android** (the APK from [Releases](https://github.com/niteshaggarwal/docket/releases), or the
   Play Store when it's listed) and open it.
5. **Sign in** with the address from step 3 and your Vikunja username and password. Docket creates a token for the
   phone; your password isn't stored.

Run `tailscale serve status` on the PC to see the address again. The PC has to be on (and signed in) for the phone
to reach it; the phone keeps working offline only for what's already on screen.

## Checking it works

- On the phone, with Tailscale connected, open the address in the browser. You should see Docket web (or Vikunja).
- If it doesn't load, check that both devices show as connected in the Tailscale app, and that the PC's server is
  running (open Docket on the PC).

## Local AI on the phone

Docket for Android can plan tasks with Ollama running on your PC. Ollama has to accept connections from other
devices for that: see [AI planning](../using/ai-planning.md#on-android).

## Letting claude.ai or ChatGPT in

Tailscale `serve` is private to your devices, which is what you want for the apps. Web assistants connect from the
internet, so they need a public address for the AI connector only: see [public address for AI](public-address.md).
