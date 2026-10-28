# CurseForSale

A Slay the Spire 2 mod. In the shop, the card removal button gets a dark purple glow around it
and a shadowy overlay on it when your deck has a curse you can remove.

It does not show:

- when every curse in your deck is Eternal, because the shop cannot remove those;
- when card removal is not available, for example after you have used it in this shop;
- when you do not have enough gold for the removal (this can be turned off in the settings).

The mod only changes what the button looks like, so it is safe in co-op.

## Install

Copy the `CurseForSale` folder into `Slay the Spire 2/mods/`, or install with Vortex.

## Settings

`CurseForSale.config.jsonc` in the mod folder sets the colours, strength, size and speed of the
glow and the overlay, and whether the effect needs enough gold. To keep your settings through
updates, copy it to `%APPDATA%\SlayTheSpire2\`; that copy is used first.

With the dev console open (any mod enables it), `cfs reload` applies changes to the config
without restarting, and `cfs diag` says whether the effect should show and why.

## Licence

MIT
