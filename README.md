# K-Desktop Assistants

Asistentes para el módulo Asistente de [K-Desktop](https://github.com/Kobayashi82/K-Desktop): personajes que viven
sobre tus ventanas. Cada uno es un paquete `.kai` que se añade desde K-Desktop (Opciones → Asistentes → «Añadir
asistente») o con un doble clic.

| Asistente | Carpeta | Estado |
|---|---|---|
| **Lander** — un pequeño módulo lunar que vuela por tus ventanas y aterriza en ellas, con su juego | [`Lander/`](Lander/) | En desarrollo (fase 2 del módulo) |

## Compilar

Los asistentes se compilan contra el SDK de K-Desktop (`src/KDesktop.Assistant.Sdk`), que de momento se toma del
código de K-Desktop: este repositorio tiene que estar clonado **dentro** del de K-Desktop, en `assistants/`.

```powershell
git clone https://github.com/Kobayashi82/K-Desktop
cd K-Desktop
git clone https://github.com/Kobayashi82/K-Desktop-Assistants assistants
cd assistants
dotnet build K-Desktop-Assistants.slnx      # cada asistente deja su .kai en bin\…\
dotnet test K-Desktop-Assistants.slnx
```

Hace falta el SDK de .NET 10.

## Probar sin instalar

```powershell
KDesktop.exe --assistant-dev "Lander\src\Lander.Assistant" --data "<carpeta de datos de prueba>"
```

Carga el paquete recién compilado y lo recarga al recompilar. `KDesktop.exe --assistant --exit --data "<carpeta>"`
lo cierra. La guía para crear uno nuevo está en `docs/assistant/crear-un-asistente.md` de K-Desktop.

## Licencia

[MIT](LICENSE).
