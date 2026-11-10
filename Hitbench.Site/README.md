# Hitbench website

A static landing page built with HTML, CSS, and a small feature-carousel script. No package installation or build step is needed.

## Preview

From the repository root:

```powershell
python -m http.server 52700 --bind 127.0.0.1 --directory .
```

Open [http://127.0.0.1:52700/Hitbench.Site/](http://127.0.0.1:52700/Hitbench.Site/).

The page references the original screenshots at `../images/*.png`. They are shared with the application README and are intentionally not copied, renamed, or re-encoded in this folder. Serve the repository root rather than this folder alone.

## Static hosting

Serve `Hitbench.Site/` and the original sibling `images/` directory with their relative layout intact. If the site's HTML, CSS, script, and brand assets are mounted at your domain root, expose the original screenshots at `/images/`. For a project subpath, preserve the equivalent sibling layout. The site has no backend or runtime CDN dependencies.

## Update links

Edit `index.html`:

- Both anchors marked `data-download` must use the same version label and matching Windows x64 ZIP URL. The current label is `Download v0.0.4-beta`; its GitHub tag and ZIP filename omit the leading `v`. Update the two labels and two URLs together. The version is static; there is no runtime GitHub API request.
- The `data-osu-post` anchor intentionally points to `https://osu.ppy.sh` for now. Replace its `href` with the post URL when available.
- The header credit links separately to [Hokichi's osu! profile](https://osu.ppy.sh/users/6607303).

## Verify changes

```powershell
node --check Hitbench.Site/carousel.js
git diff --check
```

In the served page:

1. Check 375, 768, 1024, and 1440 pixel viewport widths. Confirm readable text, wrapped actions, unclipped images, and no horizontal page overflow.
2. Select each of the six features. Confirm the screenshot, selected label, and counter agree. Check previous/next wraparound and rapid repeated input.
3. With focus on a feature selector, arrow button, or the screenshot track, use Left/Right and Home/End. Focus stays on the used control. Tab reaches only the active slide's full-image link; Enter opens it. Screenshot links retain normal keyboard behavior.
4. Scroll/swipe the feature track manually, then resize the viewport. Confirm the selected feature and counter follow the visible slide. There is no autoplay.
5. Disable JavaScript and reload. Controls stay hidden, while native horizontal scrolling and full-image links expose every feature.
6. Check visible focus outlines and reduced-motion behavior. With reduced motion enabled, carousel movement is immediate. GitHub stays `#FFFFFF` and osu! stays `#FF66AA`, including on hover.
7. Check all images, icons, and fonts load from the documented paths. Inspect the original export dialog without stretching or cropping it. Check both download links match their visible version.

## Asset sources

- App logo and Noto Sans Regular/Bold: existing `Hitbench/Resources` assets. The fonts' SIL Open Font License is included in `assets/fonts/OFL.txt`.
- Screenshots: original files in the repository's `images` directory.
- GitHub Invertocat: original black SVG from [GitHub's official logo kit](https://brand.github.com/foundations/logo), downloaded from [GitHub_Logos.zip](https://brand.github.com/GitHub_Logos.zip). The icon is used to link to the project on GitHub.
- osu! cookie: original [monochrome SVG from osu-web](https://github.com/ppy/osu-web/blob/master/public/images/layout/osu-logo-white.svg), recolored black in CSS as permitted by the [osu! brand guidelines](https://osu.ppy.sh/wiki/en/Brand_identity_guidelines). Pink `#FF66AA` comes from [osu-web's `@pink` palette value](https://github.com/ppy/osu-web/blob/master/resources/css/colors.less).

GitHub and osu! marks belong to their respective owners.
