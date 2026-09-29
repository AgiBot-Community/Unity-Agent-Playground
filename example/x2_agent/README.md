# X2 语音 Agent / Voice agent / Agent vocal

**使用指南 / User guide / Guide d’utilisation**

[简体中文](docs/zh-CN/README.md) · [English](docs/en/README.md) · [Français](docs/fr/README.md)

Python 客户端通过 WebSocket 连接 X2 模拟器。`agent.py` 使用豆包语音服务和火山方舟处理语音对话与技能调用；`demo.py` 使用固定文字和录音检查连接。

Python clients for the X2 simulator. `agent.py` provides voice conversations and skill calls through Doubao and Volcengine Ark; `demo.py` checks connections with fixed text and recordings.

Clients Python pour le simulateur X2. `agent.py` utilise Doubao et Volcengine Ark pour le dialogue vocal et les actions ; `demo.py` vérifie la connexion avec du texte fixe et des enregistrements.

## 启动 / Run / Lancement

先启动模拟器，再在本目录中执行以下命令。需要 Python 3.10+，demo 无需云服务密钥。

Start the simulator first, then run these commands in this directory. Python 3.10+ is required; the demo needs no cloud keys.

Démarrez le simulateur, puis exécutez ces commandes dans ce dossier. Python 3.10+ est requis ; la démo ne nécessite aucune clé cloud.

```powershell
python -m pip install -r requirements.txt
python demo.py
```

语音凭据、参数、技能和排障见上方三语指南。/ See the guides above for voice credentials, options, skills and troubleshooting. / Consultez les guides pour les identifiants vocaux, paramètres, actions et dépannage.

## 目录 / Layout / Organisation

```text
x2_agent/
├── agent.py            # Voice agent entry
├── demo.py             # Offline demo entry
├── .env.example        # Configuration template
├── requirements.txt
├── x2_agent/           # Implementation
├── tests/
└── docs/
    ├── zh-CN/README.md
    ├── en/README.md
    └── fr/README.md
```
