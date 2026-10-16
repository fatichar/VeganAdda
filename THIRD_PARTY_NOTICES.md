# Third-party assets

- Plus Jakarta Sans: Google Fonts / upstream authors, SIL Open Font License 1.1. Notice in `src/VeganAdda.UI/wwwroot/fonts/licenses/Plus-Jakarta-Sans-OFL.txt`.
- Material Symbols Outlined: Google, Apache License 2.0. Notice in `src/VeganAdda.UI/wwwroot/fonts/licenses/Material-Symbols-Apache.txt`.
- SkiaSharp and its native assets: MIT-licensed .NET bindings; upstream Skia has its own BSD notices distributed with its packages. NuGet retains the package license information. Review notices in release packaging.
- Stitch-supplied raster portraits and gathering artwork are used only by Development seed fixtures, copied into ignored local media storage. They are not production member identities or official event photos. Owner must verify rights before any use beyond local design/testing.

The design export remains in `design-reference/` and is excluded from the Docker image. No remote font/image requests are required by the application.
