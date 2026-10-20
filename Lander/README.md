# Lander (asistente)

El [Lander](https://github.com/Kobayashi82/Lander) de siempre como asistente de K-Desktop: un pequeño módulo lunar que
vuela por tus ventanas, aterriza en sus barras de título y tiene su propio juego (modo manual, misiones, carreras,
niveles, aspectos y logros).

Está hecho de cero a partir del Lander independiente, que sigue existiendo aparte. Lo que era del ejecutable (bandeja,
ventana de ajustes, atajo global, arranque con Windows, ventana y GPU propias, ganchos de Windows) ahora lo pone
K-Desktop; aquí queda el personaje.

## Proyectos

| Proyecto | Contenido |
|---|---|
| `src/Lander.Core` | Todo el juego en C# sin Windows (física, piloto automático, terreno, misiones, carreras, progresión), igual que en el Lander independiente. |
| `src/Lander.Assistant` | El asistente (`LanderAssistant`, implementa `IAssistant` del SDK): mundo y sensores del host, efectos, dibujo, sonido, ratón, teclado y extras. Su `Package/` es el contenido del `.kai`. |
| `tests/Lander.Core.Tests` | Las pruebas de `Lander.Core` (xUnit v3), incluidas las simulaciones largas de vida autónoma. |

`dotnet build` deja `bin\…\lander-<versión>.kai`.

## Cómo encaja en K-Desktop

- **Mundo:** el `WorldSnapshot` del host (monitores y ventanas en orden Z) se convierte en el terreno de `Lander.Core`
  en cada foto nueva; mientras está posado en una ventana que se mueve pide al host fotos a cada fotograma
  (`WatchClosely`).
- **Dibujo:** el arte de siempre (API de dibujo de WPF) en una superficie de la capa del host, que lo pasa a la GPU;
  solo se presenta un fotograma cuando cambia algo visible. Los marcadores de misión y de carrera van en superficies
  propias por detrás.
- **Ritmo:** `Tick` devuelve cuánto puede dormir: a cada fotograma mientras se mueve, 20 veces por segundo con el
  cursor cerca, 4 en reposo y 2 con el usuario ausente, y se despierta justo para parpadear.
- **Ratón:** una zona de clic con la forma del cuerpo, solo cuando el cursor se acerca: tocarlo, cogerlo y lanzarlo, y
  doble clic para pilotarlo. El clic derecho abre el menú del asistente de K-Desktop.
- **Teclado:** en modo manual pide a K-Desktop las flechas, Espacio, Esc, A, D y G (solo sin Ctrl, Alt ni Win, así que
  los atajos siguen funcionando).
- **Sonido:** los sonidos sintetizados de siempre en el mezclador del host, con panorama según dónde esté.
- **Extras:** modo manual, proponer una misión, empezar una carrera y repetirla (menú del clic derecho, y más adelante
  comandos de K-Desktop).
- **Datos:** el progreso en su carpeta de datos (`progress.json`); los ajustes llegan de K-Desktop con los mismos
  nombres que el `settings.json` del Lander independiente.

## Estado

Fase 2 del módulo Asistente de K-Desktop (ver su plan): etapa 2.1 hecha. Pendiente: repasar el comportamiento libre y
el juego con sus listas de comprobación, los ajustes y el panel en K-Desktop, los extras como comandos, importar el
Lander independiente, posarse en el contenido de las ventanas (experimental), textos en los seis idiomas y la
publicación.
