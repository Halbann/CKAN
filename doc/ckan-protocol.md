# CKAN's URL protocol

CKAN handles URLs that begin with `ckan://`. A link can focus a mod, search, or start an install.
If CKAN is running already then the current window will be used, otherwise a new window will be opened.
The GUI and the console UI both handle URLs.

This works on Windows and Linux. Adding macOS support is a bit tricker and not working yet.

There's currently no special handling for different games; it just tries to use the current instance,
even if the mod in the URL is for a different game.

The name you use for a mod in a URL is called its Identifier. You can find this in the mod info panel in the GUI.
Not only does an identifier not contain any spaces, but it can sometimes differ significantly from the mod title,
so make sure you write the actual identifier when writing a CKAN URL.

Some examples:

- 'Real Solar System' -> `RealSolarSystem`
- 'EVE - Stock Planet Configs' -> `EnvironmentalVisualEnhancements-HR`
- 'Scatterer Default Config' -> `Scatterer-config`

For convenience, identifiers in URLs can be case-insensitive. So `realsolarsystem` finds the same mod as `RealSolarSystem`.

## Operations

### Focus

    ckan://focus?mod=MechJeb2

Selects the mod in the mod list.

### Search

    ckan://search?q=engine

Puts the text in the search box and filters the list.

You can use CKAN search syntax in the query, as long as it's URI encoded. For example, you can search for 
all mods by Nertea using `@Nertea`. '%40' is the URI encoding for the '@' symbol, so it looks like this all together:

    ckan://search?q=%40Nertea

There's currently nothing in CKAN to do the URI encoding for you, but it's easy to do with a 
[website like this one](https://meyerweb.com/eric/tools/dencoder/). 

### Install

    ckan://install?mod=RealSolarSystem

Marks the mods for install. It only takes the user as far as the changeset screen, so nothing is actually
installed without user confirmation. Clicking more install links adds to the same changeset.

Repeat the mod parameter to install several mods at once.

    ckan://install?mod=RealSolarSystem&mod=ROEngines

Pin a particular version with a colon:

    ckan://install?mod=JNSQ:0.10.0

If that version isn't in the registry, then the latest compatible version is used.

## Registration

- Windows: registered per user via the registry the first time CKAN runs.
- Linux: a .desktop handler is written to ~/.local/share/applications when CKAN runs.

## Linking

A plain link works anywhere HTML does:

    <a href="ckan://install?mod=Parallax">Install Parallax</a>

Browsers usually ask permission on the first click.
