# K-Desktop Assistants

[English](README.md) · **Español**

> [!WARNING]
> **En desarrollo.** Estos asistentes y el módulo Asistente de K-Desktop todavía se están haciendo: las cosas cambian de
> una versión a otra y el SDK con el que se hacen aún no está cerrado (la 1.0 está en camino). Pruébalos, pero cuenta con
> que habrá cosas por pulir.

Asistentes para el módulo Asistente de [K-Desktop](https://github.com/Kobayashi82/K-Desktop): personajes que viven sobre
tus ventanas, a los que puedes hablar y que ejecutan tus comandos de K-Desktop con gestos que se ven. Cada uno es un
paquete `.kai`.

| Asistente | Carpeta | Qué es |
|---|---|---|
| **Lander** | [`Lander/`](Lander/) | Un pequeño módulo lunar que vuela por tus ventanas, aterriza en ellas y las mueve con su gancho. Con su propio juego (modo manual, misiones, carreras) |
| **Kobii** | [`Kobii/`](Kobii/) | Una criatura pequeña que anda, trepa y salta por tus ventanas, juega con sus juguetes y reacciona a lo que pasa en tu PC |
| **Kumo** | [`Kumo/`](Kumo/) | Una nube blandita que flota por ahí, envuelve las ventanas para llevarlas, llueve, hace arcoíris y baila con tu música |

Pip, el ejemplo de asistente sin código, está en el propio K-Desktop (`samples/CharacterAssistant`), junto al SDK.

## Instalar uno

1. En K-Desktop: **Opciones → Módulo Asistente → General → «Activar el módulo Asistente»**.
2. **Opciones → Módulo Asistente → Catálogo**: cada asistente con sus clips, lo que sabe hacer e «Instalar».

También puedes descargar un `.kai` de las [releases](https://github.com/Kobayashi82/K-Desktop/releases/tag/assistants) y
abrirlo con doble clic. Una versión nueva se instala encima de la anterior: K-Desktop la cambia en marcha y conserva el
progreso y los ajustes. Los asistentes instalados avisan cuando hay versión nueva.

## Compilar

Se compilan contra el SDK de K-Desktop (`src/KDesktop.Assistant.Sdk`), que de momento se toma del código de K-Desktop:
este repositorio tiene que estar clonado **dentro** del de K-Desktop, en `assistants/`.

```powershell
git clone https://github.com/Kobayashi82/K-Desktop
cd K-Desktop
git clone https://github.com/Kobayashi82/K-Desktop-Assistants assistants
cd assistants
dotnet build K-Desktop-Assistants.slnx -c Release   # cada asistente deja su .kai en bin\Release\…
dotnet test --solution K-Desktop-Assistants.slnx
```

Hace falta el SDK de .NET 10. El `.kai` lo genera el SDK al terminar cada compilación, con la versión de
`Package\assistant.json` en el nombre, y sale **firmado** si tu usuario de Windows tiene clave de firma (`kaisign keygen`,
ver `tools/KaiSign` en K-Desktop).

## Probar uno sin instalarlo

```powershell
KDesktop.exe --assistant-dev "Lander\src\Lander.Assistant" --data "<carpeta de datos de prueba>"
```

Carga el paquete recién compilado y lo recarga en cada compilación. La guía para hacer uno nuevo es
[Crear asistentes](https://kobayashi82.github.io/K-Desktop/es/sdk/index.html)
en K-Desktop.

## Licencia

[MIT](LICENSE).
