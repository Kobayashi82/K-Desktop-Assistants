# K-Desktop Assistants

**English** · [Español](README.es.md)

> [!WARNING]
> **Work in progress.** These assistants and the assistant module of K-Desktop are still being built: things change
> from one version to the next, and the SDK they are made with is not frozen yet (1.0 is coming). Try them, but expect
> rough edges.

Assistants for the assistant module of [K-Desktop](https://github.com/Kobayashi82/K-Desktop): characters that live on top
of your windows, that you can talk to, and that carry out your K-Desktop commands with gestures you can see. Each one is a
`.kai` package.

| Assistant | Folder | What it is |
|---|---|---|
| **Lander** | [`Lander/`](Lander/) | A little lunar module that flies around your windows, lands on them and moves them with its hook. Comes with its own game (manual mode, missions, races) |
| **Kobii** | [`Kobii/`](Kobii/) | A small creature that walks, climbs and jumps over your windows, plays with its toys and reacts to what happens on your PC |
| **Kumo** | [`Kumo/`](Kumo/) | A soft cloud that floats about, wraps windows to carry them, rains, makes rainbows and dances to your music |

Pip, the example assistant without code, lives in K-Desktop itself (`samples/CharacterAssistant`), next to the SDK.

## Installing one

1. In K-Desktop: **Settings → Assistant module → General → «Turn on the assistant module»**.
2. **Settings → Assistant module → Catalog**: each assistant with its clips, what it can do and «Install».

You can also download a `.kai` from the [releases](https://github.com/Kobayashi82/K-Desktop/releases/tag/assistants) and
double-click it. A new version installs over the old one: K-Desktop swaps it while it runs and keeps its progress and
settings. Installed assistants tell you when there is a new version.

## Building

They are built against K-Desktop's SDK (`src/KDesktop.Assistant.Sdk`), which for now comes from K-Desktop's sources: this
repository has to be cloned **inside** K-Desktop's, as `assistants/`.

```powershell
git clone https://github.com/Kobayashi82/K-Desktop
cd K-Desktop
git clone https://github.com/Kobayashi82/K-Desktop-Assistants assistants
cd assistants
dotnet build K-Desktop-Assistants.slnx -c Release   # each assistant leaves its .kai in bin\Release\…
dotnet test --solution K-Desktop-Assistants.slnx
```

It needs the .NET 10 SDK. The `.kai` is made by the SDK at the end of every build, with the version of
`Package\assistant.json` in its name, and it comes out **signed** if your Windows user has a signing key (`kaisign keygen`,
see `tools/KaiSign` in K-Desktop).

## Trying one without installing it

```powershell
KDesktop.exe --assistant-dev "Lander\src\Lander.Assistant" --data "<a test data folder>"
```

It loads the package just built and reloads it on every rebuild. The guide to make a new one is
[`docs/assistant/creating-an-assistant.md`](https://github.com/Kobayashi82/K-Desktop/blob/main/docs/assistant/creating-an-assistant.md)
in K-Desktop.

## License

[MIT](LICENSE).
