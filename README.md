# K-Desktop Assistants

Asistentes para el módulo Asistente de [K-Desktop](https://github.com/Kobayashi82/K-Desktop): personajes que viven
sobre tus ventanas. Cada uno es un paquete `.kai`.

| Asistente | Carpeta | Estado |
|---|---|---|
| **Lander** — un pequeño módulo lunar que vuela por tus ventanas y aterriza en ellas, con su juego | [`Lander/`](Lander/) | En desarrollo (fase 2 del módulo hecha) |

## Instalar un asistente

1. En K-Desktop: **Opciones → Módulo Asistente → General → «Activar el módulo Asistente»**.
2. Descarga el `.kai` de la última compilación: **Actions → CI →** la ejecución más reciente en verde **→ Artifacts**
   (`asistentes-lander-<versión>`). GitHub lo da dentro de un `.zip`: descomprímelo. Los artefactos duran 30 días; no
   hay *releases* todavía. También puedes compilarlo tú (abajo).
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
dotnet build K-Desktop-Assistants.slnx      # cada asistente deja su .kai en bin\…\ (p. ej. lander-0.5.0.kai)
dotnet test --solution K-Desktop-Assistants.slnx
```

Hace falta el SDK de .NET 10.

### CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) hace lo mismo en cada *push* a `main` (y a mano desde
**Actions → CI → Run workflow**): clona K-Desktop, pone este repositorio dentro, compila en Release con los avisos como
errores, pasa las pruebas y sube los `.kai` como artefacto. Nunca publica nada en las *releases*.

Como K-Desktop es privado, el CI necesita el secreto **`KDESKTOP_TOKEN`** (sin él, falla en el primer paso diciéndolo):

1. GitHub → **Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate new token**.
   *Repository access*: **Only select repositories** → `Kobayashi82/K-Desktop`. *Permissions*: **Contents: Read-only**
   (nada más).
2. En este repositorio: **Settings → Secrets and variables → Actions → New repository secret**, nombre
   `KDESKTOP_TOKEN`, valor el token.

Cuando caduque el token, el CI falla al clonar K-Desktop: se genera otro y se cambia el secreto.

## Probar sin instalar

```powershell
KDesktop.exe --assistant-dev "Lander\src\Lander.Assistant" --data "<carpeta de datos de prueba>"
```

Carga el paquete recién compilado y lo recarga al recompilar. `KDesktop.exe --assistant --exit --data "<carpeta>"`
lo cierra. La guía para crear uno nuevo está en `docs/assistant/crear-un-asistente.md` de K-Desktop.

## Licencia

[MIT](LICENSE).
