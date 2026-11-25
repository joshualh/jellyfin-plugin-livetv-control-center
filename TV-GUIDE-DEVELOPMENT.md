# TV Guide Categories (alpha)

Based on iEnki/Live-TV Control Center, GPL-3.0. Jellyfin 12.0 ABI / .NET 10.

Automatic category groups are read from configured M3U playlists and recognised Dispatcharr HDHR URL paths every 15 minutes. They are generated from user-authorized channel IDs and do not overwrite existing manual groups. Physical HDHR has no group-title metadata. Name/number matching is conservative. No streams or tuner configuration are changed. Do not install alongside upstream Live-TV Control Center because routes and assembly identities are shared despite a different plugin GUID.

Web and web-based Android clients: original guide receives a category dropdown; selecting a category reloads the page to reset pagination. Requires File Transformation as upstream. Experimental; verify that the client's ApiClient instance is the native guide's instance.

Stock Android TV / Fire TV: choose the TV Guide tile, select a category, then select TV Guide on the automatically opened native Live TV screen. Manual navigation remains the fallback. Native guide filtering and permission checks are inherited from upstream. Direct EPG navigation, replacing tabs and custom Back navigation are **not supported** by stock client server commands. Do not remove its original Live TV tile, which is required for fallback.

Evidence: Android TV SocketHandler.onDisplayContent routes USER_VIEW to ItemLauncherHelper.getUserViewDestination; LIVETV routes to librarySmartScreen, not Destinations.liveTvGuide. HomeRowsFragment handles the hard-coded LIVE_TV_GUIDE_OPTION_ID. No APK changes.

Build: enable Actions on fork and run TV Guide alpha build. Successful build generates a prerelease catalog at releases/download/tv-guide-alpha/manifest-alpha.json. This is experimental; compilation and real TV behavior must be verified before general rollout.
