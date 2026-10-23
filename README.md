# K-Desktop Assistants

Asistentes para el módulo Asistente de [K-Desktop](https://github.com/Kobayashi82/K-Desktop): personajes que viven
sobre tus ventanas. Cada uno es un paquete `.kai`.

| Asistente | Carpeta | Estado |
|---|---|---|
| **Lander** — un pequeño módulo lunar que vuela por tus ventanas y aterriza en ellas, con su juego | [`Lander/`](Lander/) | En desarrollo (fase 2 del módulo hecha) |

## Instalar un asistente

1. En K-Desktop: **Opciones → Módulo Asistente → General → «Activar el módulo Asistente»**.
2. Compílalo (abajo): cada compilación deja el `.kai` del asistente en su carpeta `bin`, por ejemplo
   `Lander\src\Lander.Assistant\bin\Release\net10.0-windows\win-x64\lander-<versión>.kai`. No hay *releases* todavía.
3. Doble clic en el `.kai` (o **Opciones → Módulo Asistente → Asistentes → «Añadir asistente»**) y **Instalar** en la
   ficha que se abre.

Una versión nueva se instala igual, encima de la anterior: K-Desktop la cambia en caliente y conserva el progreso y los
ajustes.

## Compilar

Los asistentes se compilan contra el SDK de K-Desktop (`src/KDesktop.Assistant.Sdk`), que de momento se toma del
código de K-Desktop: este repositorio tiene que estar clonado **dentro** del de K-Desktop, en `assistants/`.

```powershell
git clone https://github.com/Kobayashi82/K-Desktop
cd K-Desktop
git clone https://github.com/Kobayashi82/K-Desktop-Assistants assistants
cd assistants
dotnet build K-Desktop-Assistants.slnx -c Release   # cada asistente deja su .kai en bin\Release\…\ (p. ej. lander-0.5.1.kai)
dotnet test --solution K-Desktop-Assistants.slnx
```

Hace falta el SDK de .NET 10. El `.kai` lo genera el propio SDK al terminar cada compilación (Debug o Release), con la
versión de `Package\assistant.json` en el nombre; los de versiones anteriores del mismo asistente se borran.

## Probar sin instalar

```powershell
KDesktop.exe --assistant-dev "Lander\src\Lander.Assistant" --data "<carpeta de datos de prueba>"
```

Carga el paquete recién compilado y lo recarga al recompilar. `KDesktop.exe --assistant --exit --data "<carpeta>"`
lo cierra. La guía para crear uno nuevo está en `docs/assistant/creating-an-assistant.md` de K-Desktop.

## Licencia

[MIT](LICENSE).
