using System.Globalization;
using Lander.Core.Racing;

namespace Lander.Assistant;

/// <summary>
/// Los textos que el lander enseña (avisos, logros, el panel) en los seis idiomas de K-Desktop. El idioma es el de
/// K-Desktop (<see cref="KDesktop.Assistant.Sdk.IAssistantHost.Language"/>); cambiarlo reinicia el asistente, así que
/// basta con elegirlo al cargar. Lander.Core sigue con sus nombres en español, que aquí solo sirven de reserva.
/// </summary>
internal static class Texts
{
    // Orden de cada fila: es, en, fr, de, it, pt.
    private static readonly string[] Languages = ["es", "en", "fr", "de", "it", "pt"];

    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["level.title"] = ["¡Nivel {0}!", "Level {0}!", "Niveau {0} !", "Stufe {0}!", "Livello {0}!", "Nível {0}!"],
        ["level.unlocked"] = ["Desbloqueado: {0}.", "Unlocked: {0}.", "Débloqué : {0}.", "Freigeschaltet: {0}.", "Sbloccato: {0}.", "Desbloqueado: {0}."],
        ["level.keepGoing"] = [
            "Sigue aterrizando para desbloquear más cosas.", "Keep landing to unlock more things.",
            "Continue à atterrir pour débloquer d’autres choses.", "Lande weiter, um mehr freizuschalten.",
            "Continua ad atterrare per sbloccare altre cose.", "Continue pousando para desbloquear mais coisas."],
        ["achievement.title"] = ["Logro: {0}", "Achievement: {0}", "Succès : {0}", "Erfolg: {0}", "Obiettivo: {0}", "Conquista: {0}"],
        ["noMission.title"] = ["Sin misiones ahora", "No missions right now", "Pas de mission pour l’instant", "Gerade keine Missionen", "Nessuna missione ora", "Sem missões agora"],
        ["noMission.text"] = [
            "Ya hay una misión o una carrera en curso, o no hay sitio para una.",
            "There is already a mission or a race under way, or there's no room for one.",
            "Une mission ou une course est déjà en cours, ou il n’y a pas de place pour une.",
            "Es läuft schon eine Mission oder ein Rennen, oder es ist kein Platz dafür.",
            "C’è già una missione o una gara in corso, oppure non c’è spazio per una.",
            "Já há uma missão ou uma corrida em andamento, ou não há espaço para uma."],
        ["notNow.title"] = ["Ahora no", "Not now", "Pas maintenant", "Jetzt nicht", "Non ora", "Agora não"],
        ["notNow.fullscreen"] = [
            "Hay algo a pantalla completa. Prueba otra vez cuando termine.", "Something is full screen. Try again when it's done.",
            "Quelque chose est en plein écran. Réessaie quand ce sera fini.", "Etwas läuft im Vollbild. Versuch es danach noch einmal.",
            "C’è qualcosa a schermo intero. Riprova quando finisce.", "Há algo em tela cheia. Tente de novo quando terminar."],
        ["notNow.busy"] = [
            "El lander no puede salir en este momento.", "The lander can't take off right now.", "Le lander ne peut pas décoller pour le moment.",
            "Der Lander kann gerade nicht starten.", "Il lander non può partire in questo momento.", "O lander não pode sair agora."],
        ["noCircuit.title"] = ["No hay circuito", "No course", "Pas de circuit", "Keine Strecke", "Nessun percorso", "Sem circuito"],
        ["noCircuit.text"] = [
            "Hacen falta al menos dos ventanas abiertas y a la vista para poner las balizas.",
            "At least two open, visible windows are needed to place the beacons.",
            "Il faut au moins deux fenêtres ouvertes et visibles pour poser les balises.",
            "Für die Baken braucht es mindestens zwei offene, sichtbare Fenster.",
            "Servono almeno due finestre aperte e visibili per mettere le boe.",
            "São necessárias pelo menos duas janelas abertas e visíveis para colocar as balizas."],
        ["circuitGone.title"] = ["El circuito ya no está", "The course is gone", "Le circuit n’existe plus", "Die Strecke ist weg", "Il percorso non c’è più", "O circuito não existe mais"],
        ["circuitGone.text"] = [
            "Alguna de sus ventanas se ha cerrado o está tapada. Empieza una carrera nueva.",
            "One of its windows was closed or is covered. Start a new race.",
            "Une de ses fenêtres a été fermée ou est masquée. Lance une nouvelle course.",
            "Eines ihrer Fenster wurde geschlossen oder ist verdeckt. Starte ein neues Rennen.",
            "Una delle sue finestre è stata chiusa o è coperta. Inizia una nuova gara.",
            "Uma das janelas foi fechada ou está coberta. Comece uma nova corrida."],
        ["race.entry"] = ["{0} balizas · {1} s · {2}", "{0} beacons · {1} s · {2}", "{0} balises · {1} s · {2}", "{0} Baken · {1} s · {2}", "{0} boe · {1} s · {2}", "{0} balizas · {1} s · {2}"],
        ["race.record"] = [" · récord", " · record", " · record", " · Rekord", " · record", " · recorde"],
        ["medal.None"] = ["sin medalla", "no medal", "sans médaille", "keine Medaille", "nessuna medaglia", "sem medalha"],
        ["medal.Bronze"] = ["bronce", "bronze", "bronze", "Bronze", "bronzo", "bronze"],
        ["medal.Silver"] = ["plata", "silver", "argent", "Silber", "argento", "prata"],
        ["medal.Gold"] = ["oro", "gold", "or", "Gold", "oro", "ouro"],

        // Desbloqueos (Lander.Core.Progression.Unlocks).
        ["unlock.classic"] = ["Clásico", "Classic", "Classique", "Klassisch", "Classico", "Clássico"],
        ["unlock.apollo"] = ["Apolo retro", "Retro Apollo", "Apollo rétro", "Retro-Apollo", "Apollo retrò", "Apollo retrô"],
        ["unlock.capsule"] = ["Cápsula moderna", "Modern capsule", "Capsule moderne", "Moderne Kapsel", "Capsula moderna", "Cápsula moderna"],
        ["unlock.saucer"] = ["Platillo volante", "Flying saucer", "Soucoupe volante", "Fliegende Untertasse", "Disco volante", "Disco voador"],
        ["unlock.pixel"] = ["Pixel art", "Pixel art", "Pixel art", "Pixel-Art", "Pixel art", "Pixel art"],
        ["unlock.white"] = ["Humo blanco", "White smoke", "Fumée blanche", "Weißer Rauch", "Fumo bianco", "Fumaça branca"],
        ["unlock.blue"] = ["Estela azul", "Blue trail", "Traînée bleue", "Blaue Spur", "Scia blu", "Rastro azul"],
        ["unlock.rainbow"] = ["Estela arcoíris", "Rainbow trail", "Traînée arc-en-ciel", "Regenbogenspur", "Scia arcobaleno", "Rastro arco-íris"],
        ["unlock.plasma"] = ["Estela de plasma", "Plasma trail", "Traînée de plasma", "Plasmaspur", "Scia al plasma", "Rastro de plasma"],
        ["unlock.gold"] = ["Estela dorada", "Golden trail", "Traînée dorée", "Goldene Spur", "Scia dorata", "Rastro dourado"],
        ["unlock.tank"] = ["Depósito ampliado", "Bigger tank", "Réservoir agrandi", "Größerer Tank", "Serbatoio ampliato", "Tanque ampliado"],
        ["unlock.engine"] = ["Motor eficiente", "Efficient engine", "Moteur économe", "Sparsames Triebwerk", "Motore efficiente", "Motor eficiente"],
        ["unlock.legs"] = ["Patas amortiguadas", "Cushioned legs", "Pieds amortis", "Gefederte Beine", "Zampe ammortizzate", "Pernas amortecidas"],
        ["lockedAt"] = ["Nivel {0}", "Level {0}", "Niveau {0}", "Stufe {0}", "Livello {0}", "Nível {0}"],

        // Logros (Lander.Core.Progression.Achievements): nombre y descripción.
        ["ach.first-perfect"] = ["Primer contacto", "First contact", "Premier contact", "Erster Kontakt", "Primo contatto", "Primeiro contato"],
        ["ach.first-perfect.d"] = [
            "Haz un aterrizaje perfecto pilotando tú.", "Make a perfect landing flying it yourself.", "Réussis un atterrissage parfait aux commandes.",
            "Schaffe eine perfekte Landung, während du selbst steuerst.", "Fai un atterraggio perfetto pilotando tu.", "Faça um pouso perfeito pilotando você mesmo."],
        ["ach.steady-hand"] = ["Pulso firme", "Steady hand", "Main sûre", "Ruhige Hand", "Mano ferma", "Mão firme"],
        ["ach.steady-hand.d"] = [
            "Encadena 5 aterrizajes perfectos seguidos.", "Chain 5 perfect landings in a row.", "Enchaîne 5 atterrissages parfaits d’affilée.",
            "Schaffe 5 perfekte Landungen in Folge.", "Concatena 5 atterraggi perfetti di fila.", "Encadeie 5 pousos perfeitos seguidos."],
        ["ach.unstoppable"] = ["Imparable", "Unstoppable", "Inarrêtable", "Unaufhaltsam", "Inarrestabile", "Imparável"],
        ["ach.unstoppable.d"] = [
            "Encadena 12 aterrizajes perfectos seguidos.", "Chain 12 perfect landings in a row.", "Enchaîne 12 atterrissages parfaits d’affilée.",
            "Schaffe 12 perfekte Landungen in Folge.", "Concatena 12 atterraggi perfetti di fila.", "Encadeie 12 pousos perfeitos seguidos."],
        ["ach.veteran"] = ["Veterano", "Veteran", "Vétéran", "Veteran", "Veterano", "Veterano"],
        ["ach.veteran.d"] = [
            "Suma 100 aterrizajes perfectos.", "Make 100 perfect landings in total.", "Cumule 100 atterrissages parfaits.",
            "Sammle 100 perfekte Landungen.", "Totalizza 100 atterraggi perfetti.", "Some 100 pousos perfeitos."],
        ["ach.crystals"] = ["Cazador de cristales", "Crystal hunter", "Chasseur de cristaux", "Kristalljäger", "Cacciatore di cristalli", "Caçador de cristais"],
        ["ach.crystals.d"] = ["Recoge 30 cristales.", "Collect 30 crystals.", "Ramasse 30 cristaux.", "Sammle 30 Kristalle.", "Raccogli 30 cristalli.", "Colete 30 cristais."],
        ["ach.courier"] = ["Mensajero", "Courier", "Messager", "Kurier", "Corriere", "Mensageiro"],
        ["ach.courier.d"] = ["Completa 10 misiones.", "Complete 10 missions.", "Accomplis 10 missions.", "Schließe 10 Missionen ab.", "Completa 10 missioni.", "Conclua 10 missões."],
        ["ach.first-race"] = ["Bandera a cuadros", "Chequered flag", "Drapeau à damier", "Zielflagge", "Bandiera a scacchi", "Bandeira quadriculada"],
        ["ach.first-race.d"] = ["Termina una carrera.", "Finish a race.", "Termine une course.", "Beende ein Rennen.", "Finisci una gara.", "Termine uma corrida."],
        ["ach.gold"] = ["Medalla de oro", "Gold medal", "Médaille d’or", "Goldmedaille", "Medaglia d’oro", "Medalha de ouro"],
        ["ach.gold.d"] = [
            "Termina una carrera por debajo del tiempo de referencia.", "Finish a race under the target time.",
            "Termine une course sous le temps de référence.", "Beende ein Rennen unter der Referenzzeit.",
            "Finisci una gara sotto il tempo di riferimento.", "Termine uma corrida abaixo do tempo de referência."],
        ["ach.circuit-king"] = ["Rey del circuito", "King of the course", "Roi du circuit", "König der Strecke", "Re del circuito", "Rei do circuito"],
        ["ach.circuit-king.d"] = [
            "Consigue 10 medallas de oro.", "Win 10 gold medals.", "Obtiens 10 médailles d’or.", "Hole 10 Goldmedaillen.",
            "Ottieni 10 medaglie d’oro.", "Ganhe 10 medalhas de ouro."],
        ["ach.flight-hour"] = ["Una hora en el aire", "An hour in the air", "Une heure en l’air", "Eine Stunde in der Luft", "Un’ora in volo", "Uma hora no ar"],
        ["ach.flight-hour.d"] = [
            "Vuela una hora en total, en automático o pilotando.", "Fly for an hour in total, on autopilot or by hand.",
            "Vole une heure au total, en automatique ou aux commandes.", "Fliege insgesamt eine Stunde, im Autopilot oder selbst.",
            "Vola un’ora in tutto, in automatico o pilotando.", "Voe uma hora no total, no automático ou pilotando."],
        ["ach.companion"] = ["Compañero fiel", "Faithful companion", "Compagnon fidèle", "Treuer Begleiter", "Compagno fedele", "Companheiro fiel"],
        ["ach.companion.d"] = [
            "Deja que aterrice solo 500 veces.", "Let it land on its own 500 times.", "Laisse-le se poser tout seul 500 fois.",
            "Lass ihn 500 Mal von selbst landen.", "Lascialo atterrare da solo 500 volte.", "Deixe-o pousar sozinho 500 vezes."],
        ["ach.scrap"] = ["Chatarrero", "Scrap dealer", "Ferrailleur", "Schrotthändler", "Rottamaio", "Ferro-velho"],
        ["ach.scrap.d"] = [
            "Estrella el lander 25 veces. Les pasa a los mejores.", "Crash the lander 25 times. It happens to the best.",
            "Écrase le lander 25 fois. Ça arrive aux meilleurs.", "Lass den Lander 25 Mal abstürzen. Passiert den Besten.",
            "Fai schiantare il lander 25 volte. Capita ai migliori.", "Derrube o lander 25 vezes. Acontece com os melhores."],
        ["ach.ace"] = ["As del pilotaje", "Flying ace", "As du pilotage", "Fliegerass", "Asso del pilotaggio", "Ás da pilotagem"],
        ["ach.ace.d"] = ["Llega al nivel 10.", "Reach level 10.", "Atteins le niveau 10.", "Erreiche Stufe 10.", "Raggiungi il livello 10.", "Chegue ao nível 10."],
        ["ach.legend"] = ["Leyenda", "Legend", "Légende", "Legende", "Leggenda", "Lenda"],
        ["ach.legend.d"] = [
            "Llega al nivel máximo.", "Reach the top level.", "Atteins le niveau maximal.", "Erreiche die höchste Stufe.",
            "Raggiungi il livello massimo.", "Chegue ao nível máximo."],
    };

    private static int _language;

    /// <summary>Idioma de K-Desktop («es», «en»…); uno que no está, inglés.</summary>
    public static string Language
    {
        get => Languages[_language];
        set
        {
            var index = Array.IndexOf(Languages, (value ?? "es").Split('-')[0].ToLowerInvariant());
            _language = index >= 0 ? index : 1;
        }
    }

    /// <summary>Cultura con la que se escriben los números (la del idioma de K-Desktop).</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);

    /// <summary>El texto en el idioma de K-Desktop; <paramref name="fallback"/> (o la clave) si no está.</summary>
    public static string Get(string key, string? fallback = null) =>
        Table.TryGetValue(key, out var row) ? row[_language] : fallback ?? key;

    public static string Format(string key, params object[] args) => string.Format(Culture, Get(key), args);

    /// <summary>Los seis idiomas de un texto, para lo que K-Desktop elige por su cuenta (galerías de los ajustes).</summary>
    public static IEnumerable<(string Language, string Text)> All(string key) =>
        Table.TryGetValue(key, out var row) ? Languages.Zip(row) : [];

    public static string Medal(RaceMedal medal) => Get("medal." + medal);

    /// <summary>Todas las claves (para comprobar en las pruebas que cada fila tiene sus seis idiomas).</summary>
    internal static IEnumerable<(string Key, int Count)> Rows => Table.Select(p => (p.Key, p.Value.Length));
}
