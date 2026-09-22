# X2 Unity Project

中文 | English | Français

## 中文

此目录是独立的 Unity 工程根目录，可以整体复制到任意位置，不依赖外层仓库。编辑器版本为 **2022.3.62f3c1**。

首次使用：从 [Unity 官网](https://unity.com/download) 下载并安装 Unity Hub，登录账号并激活适用许可证。在 Installs → Install Editor 安装上述完整版本；若未列出，请到 [Unity 中国版本发布页](https://unity.cn/releases) 查找。独立安装 Editor 后可通过 Installs → Locate 添加 `Editor/Unity.exe`。安装 Hub 本身不会安装 Editor，不要直接替换为最新 Unity 6。

在 Unity Hub 中使用“添加磁盘上的现有项目 / Add project from disk”或“Open”，选择本 README 所在目录。若使用扫描多个项目的入口，应选择本目录的父目录。首次打开需要联网下载 Unity 注册表依赖并等待资源导入。

若出现 “No projects found. Select a folder that contains Unity projects.”，说明进入了批量 Import projects 扫描入口；请返回 Projects → Add → Add project from disk 直接添加本目录。工程根目录应同时包含 `Assets/`、`Packages/` 和 `ProjectSettings/`。

打开 `Assets/X02Competition/Scenes/scene.unity`，点击 Play；F1 显示调试面板。机器人网关可独立运行，语音对话需要另行运行兼容的 Agent 客户端。

运行前关闭其他模拟器，避免争用 `127.0.0.1:9002`；再次点击 Play 停止。需要构建 Linux 时，在 Hub 中为该编辑器添加对应后端的 Linux Build Support；工程保留的 Linux 工具链依赖不能替代该模块。

Git 管理的工程内容包括 `Assets/`（含 `.meta`）、`Packages/`、`ProjectSettings/`、本 README 和 [`.gitignore`](.gitignore)。ML-Agents 和 URDF Importer 两个随附源码包位于 `Packages/com.unity.*`，由 Unity 自动识别为嵌入式包，不需要另行安装。

**C** 循环切换摄像头；**F2** 全景、**F3** 正面跟随（默认）、**F4** 侧面跟随、**F5** 环绕视角（鼠标右键拖动、滚轮缩放）。

从源码生成 Windows 程序：停止 Play，在 File → Build Settings 中选择 Windows x86_64，将上述场景加入 Scenes In Build 并勾选，再点击 Build。运行生成的 EXE 时须保留完整输出目录；普通 Unity Build 不会生成便携单文件包。

## English

First install [Unity Hub](https://unity.com/download), sign in and activate an appropriate license. Use Installs → Install Editor to install **2022.3.62f3c1**; if absent, find it on the [Unity China releases page](https://unity.cn/releases). Register a separately installed Editor through Installs → Locate (`Editor/Unity.exe` on Windows). Hub alone does not install the Editor; do not substitute the latest Unity 6.

This directory is a standalone Unity project for **2022.3.62f3c1**. Copy the entire directory to use it independently of the parent repository. In Unity Hub, choose **Add project from disk / Open** and select this directory; a bulk project scan instead requires its parent directory. The first import needs network access for registry packages.

If Hub says “No projects found. Select a folder that contains Unity projects.”, you used the bulk Import projects entry. Return to Projects → Add → Add project from disk and select this directory, containing `Assets/`, `Packages/` and `ProjectSettings/`.

Open `Assets/X02Competition/Scenes/scene.unity` and press Play; F1 opens the debug panel. Voice conversations require a separate compatible Agent client. Git-managed project files include `Assets/` with its `.meta` files, `Packages/`, `ProjectSettings/`, this README and [`.gitignore`](.gitignore). The two bundled source packages, ML-Agents and URDF Importer, are embedded under `Packages/` and are discovered automatically.

**C** cycles cameras: **F2** overview, **F3** front follow (default), **F4** side follow, **F5** orbit (right-drag and scroll to zoom).

To build for Windows, stop Play, open File → Build Settings, select Windows x86_64, add and enable the scene in Scenes In Build, then click Build. Keep the complete output directory when running its EXE; a standard Unity Build does not create a portable single-file package.

Close other simulators to avoid conflicts on `127.0.0.1:9002`; press Play again to stop. For Linux builds, add Linux Build Support for your backend to this Editor through Hub. The retained Linux toolchain dependencies do not replace that module.

## Français

Installez d’abord [Unity Hub](https://unity.com/download), connectez-vous et activez une licence adaptée. Installez **2022.3.62f3c1** via Installs → Install Editor ; si cette version est absente, consultez les [versions Unity Chine](https://unity.cn/releases). Ajoutez un éditeur installé séparément via Installs → Locate (`Editor/Unity.exe` sous Windows). Hub seul n’installe pas l’éditeur ; ne le remplacez pas directement par Unity 6.

Ce dossier est un projet Unity autonome pour **2022.3.62f3c1**. Copiez le dossier entier pour l’utiliser indépendamment du dépôt parent. Dans Unity Hub, choisissez **Add project from disk / Open** et sélectionnez ce dossier ; pour une recherche de plusieurs projets, sélectionnez son dossier parent. Le premier import nécessite un accès réseau pour les paquets du registre.

Si Hub affiche « No projects found. Select a folder that contains Unity projects. », vous avez utilisé Import projects. Revenez à Projects → Add → Add project from disk et sélectionnez ce dossier contenant `Assets/`, `Packages/` et `ProjectSettings/`.

Ouvrez `Assets/X02Competition/Scenes/scene.unity` puis cliquez sur Play ; F1 affiche le panneau de débogage. Les conversations vocales nécessitent un client Agent compatible distinct. Le contenu versionné comprend `Assets/` avec ses fichiers `.meta`, `Packages/`, `ProjectSettings/`, ce README et [`.gitignore`](.gitignore). Les deux paquets sources fournis, ML-Agents et URDF Importer, sont intégrés dans `Packages/` et détectés automatiquement.

**C** parcourt les caméras : **F2** vue générale, **F3** suivi frontal (par défaut), **F4** suivi latéral, **F5** vue orbitale (glisser avec le bouton droit et zoomer avec la molette).

Pour compiler pour Windows, arrêtez Play, ouvrez File → Build Settings, sélectionnez Windows x86_64, ajoutez et cochez la scène dans Scenes In Build, puis cliquez sur Build. Conservez tout le dossier de sortie pour lancer son EXE ; une compilation Unity standard ne crée pas de paquet portable à fichier unique.

Fermez les autres simulateurs pour éviter un conflit sur `127.0.0.1:9002` ; cliquez de nouveau sur Play pour arrêter. Pour compiler sous Linux, ajoutez Linux Build Support pour votre backend à cet éditeur via Hub. Les dépendances de compilation Linux conservées ne remplacent pas ce module.
