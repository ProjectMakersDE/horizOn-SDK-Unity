# [1.12.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.11.0...v1.12.0) (2026-10-23)


### Bug Fixes

* accept compact achievements and refresh atomic reward state ([8a84a76](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/8a84a76dbaf5d9b149b12046ad2d3bf7c3bf6428))
* **achievements:** preserve nullable fields in defensive copies ([7d291d5](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/7d291d5d389db1d8aa4b8bf69f10b22e96078baa))
* **docs:** document atomic achievement rewards ([21d61cc](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/21d61cc772a1037f849ed8aad0eef886df3bbb7b))
* **gift-codes:** bind fresh receipts to original count and revision ([9d0b68e](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/9d0b68e3eb8708075c9bf6aba0db35017396d0b5))
* **gift-codes:** display unresolved protocol errors in example UI ([fb8bcc3](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/fb8bcc36d8a887dd86b08075e9d9ec904c97bcf3))
* **gift-codes:** preserve pure payloads across values invalidation (TASK-1007) ([650bb89](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/650bb89891c21ce5fb7df104d44b79098a031ff8))
* **gift-codes:** serialize example UI preparation before consumption ([0b98fba](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/0b98fbafe3fbea2430f23201ebdcdc065f1513d5))
* **groups:** recover durable sample journals after process death ([30623ab](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/30623abc34f02542db75bef66c8b4b6f84b6c0d1))
* import native SDK samples in the release test host ([f1bf87e](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/f1bf87e35a8aad07822351e639aca78de0eec05c))
* **inbox:** preserve optional reward presence in Unity cache copies ([84c0e3f](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/84c0e3fdafeda13953ffae82bf993f2529a6f4d6))
* **inbox:** reject whitespace in exact entry and source UUIDs ([02b59db](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/02b59dbaaeaa56449f705fcf15250ee94ad93eac))
* **inbox:** retain legacy group acceptance references ([902aed7](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/902aed7f6463f33b99570fb62942061198b4a0a8))
* **inbox:** validate new sample claims before intent persistence ([5529780](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/55297808ee2e13d267f093308f12d8bcc01f5f7b))
* **items:** preserve configurable quotas and retained inventory ([#37](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/issues/37)) ([5450643](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/54506431e4540d74de3908dff761b236e933467c))
* **items:** retain exclusive journal ownership through recovery ([fd76106](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/fd76106d1fdb97d9db9c8914220be7fb3ce8897b))
* keep budgeted Groups string decoding distinct from LINQ delegates ([228df89](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/228df89efeddac27190221b78e7c83ec3e06e92c))
* **player-values:** preserve compact replay and configurable catalog contracts ([#36](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/issues/36)) ([0abb9fd](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/0abb9fdadc79f6b6a2be539513fda9d523f25118))
* preserve configured quotas and compact match sources across SDK consumers ([0de77d5](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/0de77d5c167afb2d8f84fbe8107b8703332f56fc))
* preserve match watermarks and parse actual groups response envelopes ([0c96f35](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/0c96f353f903f618a4226da02bba53939c5086eb))
* preserve source bindings and configured sample member pages ([5e08baa](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/5e08baad168c9f74810facedc43cbc3531ba9432))
* **readme:** link the horizOn Discord with its real invite code (TASK-1046) ([4f5b838](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/4f5b838a33e87f727c050b9d362ea312bb7d0170))
* **realtime:** correlate subscription replies and acknowledge resync (TASK-995) ([d0e8048](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/d0e804809c7c8d746faaed3fb73977b022726075))
* require an eligible email sample recipient and bound OAuth claims ([885b7b2](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/885b7b2b8d1e7fe1208c709787dd40dd9a5bb082))
* retain group revision floors after configurable quota lowering ([144d719](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/144d719144d95af03b0ee49e6cb87c0e36c26f50))
* retain single-argument JSON string decoder overload ([7a60f6d](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/7a60f6d142b4abb7b2381244749353ce1087eb40))
* **statistics:** expose safe batched Unity counters (TASK-1005) ([1f9242f](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/1f9242f497bd5bf412f54ae98882ad026804839c))
* **statistics:** honor server configured catalog limits ([76d543e](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/76d543e413e6ef5fa72420bf3febacb2df51f977))
* **statistics:** preserve project context and saved player identity (TASK-1005) ([a88090c](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/a88090c1cf4956ddc569ca7f894936c985368066))
* **transport:** honor account waits and feature limits (TASK-996) ([d352de9](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/d352de97c5dc4f30055e476b95803c036878586c))
* validate compact unlock result consistency ([f418f6b](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/f418f6be234a702b632fa18571a35568f553184e))
* **values:** serialize signed amounts independently of device locale ([15cf98f](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/15cf98f0b0127e5f138b4c973745c2450d0b6f83))


### Features

* **achievements:** add scoped Unity progress and Inbox rewards ([641d05a](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/641d05af67e26db9e169b854995d114b31fe376f))
* add revision-safe asynchronous matches and durable sample ([471b640](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/471b6408cb0153a418be57fbfe2204a8f7175520))
* add revision-safe Items SDK and durable sample ([284d721](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/284d7214199487e4fc029a3fbcbf649234aa861d))
* **chat:** keep history on device with bounded shared server windows ([78338dd](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/78338dd1f9dbf286c21634775d19a329c47d5e65))
* **friends:** add scoped Unity social graph manager ([85ad867](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/85ad867ce55d053600d8585d2b29c36cc5614423))
* **gift-codes:** add replay-safe Unity value rewards (TASK-1007) ([d4a4cea](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/d4a4cea79cbb9cbf026e5ec8b049b25f115481cb))
* **gift-codes:** persist and retry prepared V2 campaign intents ([31d380b](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/31d380b10b9a7beca9e49c079a08310326f27a69))
* **groups:** add session-safe manager and durable two-player example ([e1caad1](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/e1caad1de0616f8c1cda250bfc20cdec427d2dfb))
* **inbox:** add bounded Unity snapshots and atomic reward claims ([740a54c](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/740a54c95f9dd0c36c7cff91269de0f483ca71ed))
* **inbox:** support source notices and configurable mailbox limits ([bc46a83](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/bc46a835872f291061dfe12a1fb62128eaad1464))
* **marketplace:** add typed ten-route app consumer and exact wire codec ([14ae97d](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/14ae97d40eebc629cff2c0a008520bce72d39206))
* **marketplace:** persist original intents and verify native escrow recovery ([c3092e4](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/c3092e420f63fcdf985e9b8b0c1477fa3948627b))
* **realtime:** add bounded native and WebGL subscriptions (TASK-995) ([8324492](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/83244928799ae4e35adce7a95e1061ae6fbd722d))
* **shared-objects:** add guarded Unity adapter and durable examples ([2125bd1](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/2125bd1695e5531a43d114dac4c5e0e1f71b1004))
* **values:** add atomic player value operations and safe retry context ([172bec6](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/172bec60b2ca7d43b901f0005902610d675d15ae))

# [1.11.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.10.1...v1.11.0) (2026-10-10)


### Features

* **cloud-save:** revision-checked saves for several devices ([2eb68b5](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/2eb68b5a6a2dd3c543c65a93b4fad12d6c2b8e95))

## [1.10.1](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.10.0...v1.10.1) (2026-10-08)


### Bug Fixes

* **samples:** import demo without render pipeline requirements ([#12](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/issues/12)) ([5005aef](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/5005aef054d08e3c3c2fd7f0ce5e2eeae5818152))

# [1.10.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.9.0...v1.10.0) (2026-10-02)


### Bug Fixes

* **email:** serialize template variables as JSON object ([e1ec186](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/e1ec1867c4e81917871f1f3df14106a20c6f1445))


### Features

* **validated-actions:** optional run start context and sus result ([2d4faf9](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/2d4faf9b85cd848673980045d5b33158633200a9))

# [1.9.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.8.6...v1.9.0) (2026-10-01)


### Bug Fixes

* bind cloud saves to sessions and load binary data via POST ([1a23308](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/1a23308fcd96d2bcd27b50f19b31fc73c4c49808))
* **gift-codes:** send the player session when redeeming a code ([88f50c3](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/88f50c3216ac0360aec16d4acd4ab861a2638bd2))
* **network:** clear error when still rate limited after the last retry (TASK-885) ([ad14c5a](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/ad14c5a8c390092dccc688518435242e4e09819d))
* **validated-actions:** report NOT_SUPPORTED for the simpleServer's generic 404 NOT_FOUND ([37e4776](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/37e4776c494af8a2abaa2d2b48659ddd20798a67))


### Features

* **player-profile:** add PlayerProfileManager, profile on leaderboard entries and gift code unlocks ([f759973](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/f7599739e28476b10f5f049eda7b10d60784bc7b))
* **validated-actions:** add server-owned player state (GetState, CurrentState) (TASK-887) ([6b8cbdf](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/6b8cbdfa6ade705a950ccd85c3d7e62aa6ea4df0))
* **validated-actions:** add ValidatedActionsManager for server-checked runs (TASK-883) ([4c4d66a](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/4c4d66a6e665be5847c5f3fea8a779702d455e12))
* **validated-actions:** upload input log evidence and expose PLAYER_BANNED (TASK-888) ([5f6d459](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/5f6d459b97ee5a997afa3b917acf327a802821e7))

## [1.8.6](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.8.5...v1.8.6) (2026-09-28)


### Bug Fixes

* align anonymous signup and restore with server tokens ([#6](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/issues/6)) ([b3319a8](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/b3319a8b4da43aa68cdc96d641da6c12e86f2c60))
* **ci:** allow manual SDK release workflow runs ([#8](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/issues/8)) ([45af670](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/45af670264af4f09f8b4e2730c88a7ec5706cb17))

## [1.8.5](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.8.4...v1.8.5) (2026-09-28)


### Bug Fixes

* **ci:** isolate Unity leaderboard editor test ([cf2cad2](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/cf2cad29c64e2e27510675520f5d87ab0a868cca))
* **ci:** isolate Unity test project dependencies ([82fede6](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/82fede66522d0a5985441b66c335697dea9495ca))
* **ci:** resolve Unity package for editor tests ([efe6490](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/efe6490086e2cdb9750c6b77f9ed4783a9826301))
* **ci:** restore Unity project assets directory ([fcc50be](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/fcc50bed727fa33b565ffa3a63ab6da7a435610a))
* **ci:** run actual Unity editor transport tests ([d512ecf](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/d512ecf75c3a89c8f45985d44f3c1081f60d6843))
* **ci:** run transport tests on develop ([8bcbabd](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/8bcbabdd47f6d9fc33d2702d96e6e84d4c999ba4))
* **ci:** use hidden Unity editor project path ([876baa5](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/876baa579e9929a65b0fa6602b14b9aceb83dcf6))
* **samples:** add missing UnityDefaultRuntimeTheme.tss and document HorizonConfig gitignore ([0a53c8f](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/0a53c8f5d2fdac6b718d1438f969b47ffe5506fb))
* **security:** test signed leaderboard transport ([6d95ffa](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/6d95ffad8dec2111914d092e6881c90270d35c39))

## [1.8.4](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.8.3...v1.8.4) (2026-09-01)


### Bug Fixes

* **security:** attach sessions to leaderboard submits ([9fc9610](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/9fc96108982f5ee104da9fdedaa6d88822f6fae8))

## [1.8.3](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.8.2...v1.8.3) (2026-07-14)


### Bug Fixes

* **changelog:** restore v1.0.5 and v1.0.6 history ([51791fd](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/51791fdda52e11a3886c8f218a40946d4e3b0018))

## [1.8.2](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.8.1...v1.8.2) (2026-07-13)


### Bug Fixes

* **config:** decouple API key obfuscation from the importing machine ([6c4a149](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/6c4a1494f8efd548efce72dba4f293c5afd05f2c))
* **user:** keep the anonymous recovery token across sign-in ([ffcefa6](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/ffcefa692c621f13a87e6cdd28c1b45b096bf16d))

## [1.8.1](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.8.0...v1.8.1) (2026-07-10)


### Bug Fixes

* reset static SDK state for Fast Enter Play Mode (Unity 6.6 domain reload) ([10e6744](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/10e674437ea0c7b0a0081ac295157d2b5d4ffaf3))

# [1.8.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.7.0...v1.8.0) (2026-06-24)


### Features

* **localization:** add LocalizationManager, responses and example ([12c6c73](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/12c6c737aa6d06ad2407f48acb20d624e563cf43))

# [1.7.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.6.0...v1.7.0) (2026-06-17)


### Features

* distribute SDK as a UPM git-url package + license-free .unitypackage build ([#3](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/issues/3)) ([27b344c](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/27b344c321c90fa1128d8d8b3c85b2807b996835))

# [1.6.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.5.1...v1.6.0) (2026-05-22)


### Features

* add multi-board leaderboard helpers ([0004e46](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/0004e4640744380338faf67cb12193ac41adba49))

## [1.5.1](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.5.0...v1.5.1) (2026-05-14)


### Bug Fixes

* **sdk:** add per-feature minimal examples and Hello horizOn entry (TASK-217) ([9a22130](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/9a2213023ff3c28192f74ca58a6bb88a86703d2f))

# [1.5.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.4.0...v1.5.0) (2026-04-20)


### Features

* **apple-signin:** apple sign-in support in unity sdk ([48ef98a](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/48ef98a35ce513e67ddf57de766b8e58e05cac50))

# [1.4.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.3.0...v1.4.0) (2026-04-11)


### Features

* add email sending manager with send, cancel, and status methods ([0c87646](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/0c8764659dc13db03866977609d79565ace6f8b6))

# [1.3.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.2.0...v1.3.0) (2026-02-23)


### Features

* **crash:** add crash reporting section to Example UI ([b8089a6](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/b8089a671b9eb0e4d2adc6552efcad81b1d4b117))
* **crash:** add crash reporting to Unity SDK ([5e81609](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/5e816094e9ff73baa9edbeecb9b84393b60b1dce))

# [1.2.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.1.0...v1.2.0) (2026-02-20)


### Bug Fixes

* add missing googleRedirectUri to Google auth requests ([0190548](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/01905486c36f64fe622647602a9d592c977002f5))
* broken links, wrong GitHub URL, and version inconsistencies ([bf78568](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/bf78568c73a068f6adf8bec7f85f0d927705bcce))


### Features

* add Google auth to ExampleUI, docs, and auto-version in releases ([e71e536](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/e71e5366949a5f77b095a032a332ec18be83ef4b))
* skip ping for single-host configs (loadbalancer support) ([d2b7d71](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/d2b7d718419e492fea49a8dbab27838a0724f883))

# [1.1.0](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.0.6...v1.1.0) (2026-02-13)


### Features

* add semantic-release CI/CD with changelog and sync workflow ([b78e26a](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/b78e26a0a2ee6343e68ad27a38c3bf92cc879106))

## [1.0.6](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.0.5...v1.0.6) (2025-12-07)


### Bug Fixes

* change public field names to camelCase in user-related classes ([8e544d7](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/8e544d79ecd26d92bef8f96c4658e2558c2efac2))

## [1.0.5](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/compare/v1.0.4...v1.0.5) (2025-12-07)


### Bug Fixes

* add permissions for release creation ([aa865ba](https://github.com/ProjectMakersDE/horizOn-SDK-Unity/commit/aa865ba9b6975c9d8233440a158e5f6439ccbf68))

# Changelog

All notable changes to this project will be documented in this file.

This file is automatically updated by [semantic-release](https://github.com/semantic-release/semantic-release).
