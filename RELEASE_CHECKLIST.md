# NoBloat Dimmer release checklist

## 1. Build and test on Windows

1. Open PowerShell in the `NoBloatDimmer` source folder.
2. Run `./publish-release.bat`.
3. Test `publish/win-x64/NoBloatDimmer.exe` before uploading anything.
4. Confirm window dragging, full screen, 5% arrow key steps, the blackout cycle, and the website button.
5. Copy the printed SHA256 and Website size values.

## 2. Publish on GitHub

1. Commit the updated source files to `nobloatguy/NoBloatDimmer`.
2. Create tag `v0.2.0-beta.3` from that commit.
3. Create a GitHub release named `NoBloat Dimmer v0.2.0 Beta 3`.
4. Keep the GitHub prerelease option off so the website's `/releases/latest/` link selects this release.
5. Upload `publish/NoBloatDimmer-win-x64.zip`.
6. Paste the release notes and SHA256 into the release description.
7. Publish the release and test its download.

## 3. Update NoBloatTools.com

The three website download links already use the stable GitHub asset name and should redirect to the new latest release automatically.

1. In `index.html`, change the release line from `v0.1.0 Beta` to `v0.2.0 Beta 3`.
2. Replace `62.5 MB` with the Website size printed by the release script.
3. Deploy the entire website bundle as a complete replacement in Cloudflare Pages.
4. Open `nobloattools.com`, press Ctrl + F5, and test all three download buttons.
