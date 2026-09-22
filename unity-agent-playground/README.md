# X2 Unity Project

[中文](#中文) | [English](#english) | [Français](#français)

## 中文

此目录是独立的 Unity 工程根目录，可以整体复制到任意位置，不依赖外层仓库。编辑器版本为 **2022.3.62f3c1**。

### 安装指定的旧版编辑器

1. 从 [Unity 官网](https://unity.com/download) 安装 Unity Hub，登录账号并激活适用许可证。Hub 用于管理项目和编辑器，仍需单独安装下述 Editor。
2. 打开 [Unity 中国发布页](https://unity.cn/releases)，选择 2022 系列并查找 `2022.3.62f3` 的中国版下载项。完整版本应为 **2022.3.62f3c1**，修订号为 `1623fc0bbb97`；发布页标题可能不显示 `c1`。
3. 优先使用页面提供的 Hub 安装入口。入口不可用时，Windows 用户可下载[官方独立安装程序](https://download.unitychina.cn/download_unity/1623fc0bbb97/Windows64EditorInstaller/UnitySetup64.exe)，安装后通过 **Installs → Locate** 选择安装目录中的 `Editor/Unity.exe`。其他系统在发布页选择对应系统和架构的安装包。
4. 确认 Hub 显示 **2022.3.62f3c1** 后再打开工程。该版本可与其他编辑器共存；不要将全球版 `2022.3.62f3` 直接当作同一版本。

其他全球版旧编辑器可从 **Installs → Install Editor → Archive** 进入[全球归档](https://unity.com/releases/editor/archive)，选择版本并点击安装到 Hub。官方步骤见 [Editor 安装与 Locate](https://docs.unity.com/en-us/hub/add-editor)。上述中国版下载链接已于 2026-09-23 联网核对，未在本次文档更新中下载安装程序。

### 添加工程并运行

在 Unity Hub 中使用“添加磁盘上的现有项目 / Add project from disk”或“Open”，选择本 README 所在目录。若使用扫描多个项目的入口，应选择本目录的父目录。首次打开需要联网下载 Unity 注册表依赖并等待资源导入。

若出现 “No projects found. Select a folder that contains Unity projects.”，说明进入了批量 Import projects 扫描入口；请返回 Projects → Add → Add project from disk 直接添加本目录。工程根目录应同时包含 `Assets/`、`Packages/` 和 `ProjectSettings/`。

打开 `Assets/X02Competition/Scenes/scene.unity`，点击 Play；F1 显示调试面板。机器人网关可独立运行，语音对话需要另行运行兼容的 Agent 客户端。

运行前关闭其他模拟器，避免争用 `127.0.0.1:9002`；再次点击 Play 停止。首次验证可先使用 F1 面板的技能按钮，再连接 Agent。

Git 管理的工程内容包括 `Assets/`（含 `.meta`）、`Packages/`、`ProjectSettings/`、本 README 和 [`.gitignore`](.gitignore)。ML-Agents 和 URDF Importer 两个随附源码包位于 `Packages/com.unity.*`，由 Unity 自动识别为嵌入式包，不需要另行安装。

**C** 循环切换摄像头；**F2** 全景、**F3** 正面跟随（默认）、**F4** 侧面跟随、**F5** 环绕视角（鼠标右键拖动、滚轮缩放）。

### 构建与模块

停止 Play，在 **File → Build Settings** 中选择 **Windows x86_64**，将上述场景加入 **Scenes In Build** 并勾选。在 Player Settings 中启用 `Run In Background`，点击 **Build** 并选择 `Assets/` 之外的专用空目录。运行和分发时保留完整输出目录；普通 Unity Build 不会生成便携单文件包。

Linux 构建需要与脚本后端匹配的 **Linux Build Support**，工程内的工具链包不能替代该模块。Hub 安装的 Editor 可通过 **Manage → Add modules** 添加模块；独立安装后用 Locate 添加的 Editor 通常不支持此操作。若需通过 Hub 管理模块，请按[官方模块说明](https://docs.unity.com/en-us/hub/add-modules)通过 Hub 重新安装所需版本。

## English

### Install the required older Editor

1. Install [Unity Hub](https://unity.com/download), sign in and activate an appropriate license. Hub manages projects and Editors; install the Editor separately as follows.
2. Open the [Unity China releases page](https://unity.cn/releases), select the 2022 series and find the China download for `2022.3.62f3`. The full version must be **2022.3.62f3c1**, revision `1623fc0bbb97`; the listing title may omit `c1`.
3. Prefer the page's Hub installation option if available. Otherwise, Windows users can use the [official standalone installer](https://download.unitychina.cn/download_unity/1623fc0bbb97/Windows64EditorInstaller/UnitySetup64.exe), then select the installed `Editor/Unity.exe` with **Installs → Locate**. Select the appropriate OS and architecture on the releases page for other systems.
4. Confirm **2022.3.62f3c1** in Hub before opening the project. It can coexist with other Editors; the global `2022.3.62f3` installer is not the same version.

For other older global releases, use **Installs → Install Editor → Archive** to open the [global archive](https://unity.com/releases/editor/archive), select a version and install through Hub. See the official [Editor installation and Locate guide](https://docs.unity.com/en-us/hub/add-editor). The China download link was checked online on 2026-09-23; the installer was not downloaded or run for this documentation update.

### Add and run the project

This directory is a standalone Unity project for **2022.3.62f3c1**. Copy the entire directory to use it independently of the parent repository. In Unity Hub, choose **Add project from disk / Open** and select this directory; a bulk project scan instead requires its parent directory. The first import needs network access for registry packages.

If Hub says “No projects found. Select a folder that contains Unity projects.”, you used the bulk Import projects entry. Return to Projects → Add → Add project from disk and select this directory, containing `Assets/`, `Packages/` and `ProjectSettings/`.

Open `Assets/X02Competition/Scenes/scene.unity` and press Play; F1 opens the debug panel. Voice conversations require a separate compatible Agent client. Git-managed project files include `Assets/` with its `.meta` files, `Packages/`, `ProjectSettings/`, this README and [`.gitignore`](.gitignore). The two bundled source packages, ML-Agents and URDF Importer, are embedded under `Packages/` and are discovered automatically.

**C** cycles cameras: **F2** overview, **F3** front follow (default), **F4** side follow, **F5** orbit (right-drag and scroll to zoom).

Close other simulators to avoid conflicts on `127.0.0.1:9002`; press Play again to stop. For an initial check, test skills with the F1 buttons before connecting an agent.

### Build and choose modules

Stop Play, open **File → Build Settings**, select **Windows x86_64**, and add and enable the scene in **Scenes In Build**. Enable `Run In Background` in Player Settings, select **Build**, and choose a dedicated empty directory outside `Assets/`. Keep the complete output directory when running or distributing the application; a standard Unity Build does not create a portable single-file package.

Linux builds require **Linux Build Support** matching the scripting backend; the project's toolchain packages do not replace that module. For Hub-installed Editors, use **Manage → Add modules**. Editors installed separately and registered with Locate usually do not support this action. To manage modules through Hub, reinstall the required version through Hub as described in the [official module guide](https://docs.unity.com/en-us/hub/add-modules).

## Français

### Installer l’ancien éditeur requis

1. Installez [Unity Hub](https://unity.com/download), connectez-vous et activez une licence adaptée. Hub gère les projets et les éditeurs ; installez ensuite l’éditeur comme indiqué ci-dessous.
2. Ouvrez les [versions Unity Chine](https://unity.cn/releases), choisissez la série 2022 et trouvez le téléchargement chinois de `2022.3.62f3`. La version complète doit être **2022.3.62f3c1**, révision `1623fc0bbb97` ; le titre peut omettre `c1`.
3. Privilégiez l’installation via Hub si la page la propose. Sinon, sous Windows, utilisez le [programme officiel autonome](https://download.unitychina.cn/download_unity/1623fc0bbb97/Windows64EditorInstaller/UnitySetup64.exe), puis sélectionnez `Editor/Unity.exe` dans **Installs → Locate**. Pour les autres systèmes, choisissez le système et l’architecture sur la page des versions.
4. Vérifiez **2022.3.62f3c1** dans Hub avant d’ouvrir le projet. Cette version peut coexister avec d’autres éditeurs ; la version mondiale `2022.3.62f3` utilise un autre programme d’installation.

Pour d’autres anciennes versions mondiales, ouvrez **Installs → Install Editor → Archive**, puis les [archives mondiales](https://unity.com/releases/editor/archive), choisissez une version et installez-la via Hub. Consultez le [guide officiel d’installation et Locate](https://docs.unity.com/en-us/hub/add-editor). Le lien chinois a été vérifié en ligne le 2026-09-23 ; le programme n’a pas été téléchargé ni exécuté pour cette mise à jour documentaire.

### Ajouter et exécuter le projet

Ce dossier est un projet Unity autonome pour **2022.3.62f3c1**. Copiez le dossier entier pour l’utiliser indépendamment du dépôt parent. Dans Unity Hub, choisissez **Add project from disk / Open** et sélectionnez ce dossier ; pour une recherche de plusieurs projets, sélectionnez son dossier parent. Le premier import nécessite un accès réseau pour les paquets du registre.

Si Hub affiche « No projects found. Select a folder that contains Unity projects. », vous avez utilisé Import projects. Revenez à Projects → Add → Add project from disk et sélectionnez ce dossier contenant `Assets/`, `Packages/` et `ProjectSettings/`.

Ouvrez `Assets/X02Competition/Scenes/scene.unity` puis cliquez sur Play ; F1 affiche le panneau de débogage. Les conversations vocales nécessitent un client Agent compatible distinct. Le contenu versionné comprend `Assets/` avec ses fichiers `.meta`, `Packages/`, `ProjectSettings/`, ce README et [`.gitignore`](.gitignore). Les deux paquets sources fournis, ML-Agents et URDF Importer, sont intégrés dans `Packages/` et détectés automatiquement.

**C** parcourt les caméras : **F2** vue générale, **F3** suivi frontal (par défaut), **F4** suivi latéral, **F5** vue orbitale (glisser avec le bouton droit et zoomer avec la molette).

Fermez les autres simulateurs pour éviter un conflit sur `127.0.0.1:9002` ; cliquez de nouveau sur Play pour arrêter. Commencez par tester les actions avec les boutons F1, puis connectez un agent.

### Compiler et choisir les modules

Arrêtez Play, ouvrez **File → Build Settings**, choisissez **Windows x86_64**, puis ajoutez et cochez la scène dans **Scenes In Build**. Activez `Run In Background` dans Player Settings, cliquez sur **Build** et choisissez un dossier vide dédié hors de `Assets/`. Conservez toute la sortie pour exécuter ou distribuer l’application ; une compilation standard ne crée pas de paquet portable à fichier unique.

Linux nécessite **Linux Build Support** adapté au backend de script ; les paquets du projet ne remplacent pas ce module. Pour un éditeur installé via Hub, utilisez **Manage → Add modules**. Un éditeur installé séparément puis ajouté avec Locate ne permet généralement pas cette opération. Pour gérer les modules via Hub, réinstallez la version requise via Hub selon le [guide officiel des modules](https://docs.unity.com/en-us/hub/add-modules).
