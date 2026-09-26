using FearMe.AI;
using FearMe.Player;
using UnityEngine;

namespace FearMe.Core
{
    // IMGUI on purpose: no font assets or TextMeshPro import needed, so the
    // demo scene works the moment it is generated.
    public class DemoHUD : MonoBehaviour
    {
        [SerializeField] private ObjectiveTracker objectives;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private GameOverController gameFlow;

        private GUIStyle textStyle;
        private GUIStyle centerStyle;

        private void BuildStyles()
        {
            if (textStyle != null) return;

            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperLeft
            };
            textStyle.normal.textColor = new Color(0.85f, 0.83f, 0.8f);

            centerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 42,
                alignment = TextAnchor.MiddleCenter
            };
            centerStyle.normal.textColor = new Color(0.8f, 0.1f, 0.1f);
        }

        private void DrawEndCard(bool escaped)
        {
            centerStyle.normal.textColor = escaped
                ? new Color(0.82f, 0.80f, 0.74f)
                : new Color(0.75f, 0.08f, 0.08f);

            float third = Screen.height / 3f;

            GUI.Label(
                new Rect(0f, third - 40f, Screen.width, 80f),
                escaped ? "YOU ESCAPED" : "IT FOUND YOU",
                centerStyle);

            if (!escaped) return;

            GUIStyle thanks = new GUIStyle(centerStyle) { fontSize = 26 };
            thanks.normal.textColor = new Color(0.7f, 0.68f, 0.64f);
            GUI.Label(
                new Rect(0f, third + 50f, Screen.width, 40f),
                "THANK YOU FOR PLAYING THE DEMO",
                thanks);

            GUIStyle note = new GUIStyle(centerStyle) { fontSize = 18 };
            note.normal.textColor = new Color(0.45f, 0.44f, 0.42f);
            GUI.Label(
                new Rect(0f, third + 100f, Screen.width, 30f),
                "returning to the menu",
                note);
        }

        // Your own bleed-out, and a nudge when a teammate is waiting for help.
        private void DrawVitals()
        {
            PlayerController local = PlayerRegistry.Local;
            if (local == null) return;

            PlayerVitals mine = local.GetComponent<PlayerVitals>();
            if (mine != null && mine.IsDown && !mine.IsDead)
            {
                int left = Mathf.CeilToInt(mine.BleedOutRemaining);
                string headline = mine.Captivity switch
                {
                    Captivity.Dragged => $"IT HAS YOU - {left}s",
                    Captivity.Caged => $"CAGED - {left}s",
                    _ => $"DOWN - {left}s"
                };
                string help = mine.Captivity switch
                {
                    Captivity.Dragged => "your partner has to stop it",
                    Captivity.Caged => "your partner has to break the lock",
                    _ => "your partner has to reach you"
                };

                GUIStyle downed = new GUIStyle(centerStyle) { fontSize = 30 };
                downed.normal.textColor = new Color(0.75f, 0.1f, 0.1f);
                GUI.Label(new Rect(0f, Screen.height * 0.62f, Screen.width, 40f), headline, downed);

                GUIStyle sub = new GUIStyle(centerStyle) { fontSize = 18 };
                sub.normal.textColor = new Color(0.6f, 0.58f, 0.55f);
                GUI.Label(new Rect(0f, Screen.height * 0.62f + 40f, Screen.width, 30f), help, sub);
                return;
            }

            foreach (PlayerController other in PlayerRegistry.All)
            {
                if (other == null || other == local) continue;

                PlayerVitals vitals = other.GetComponent<PlayerVitals>();
                if (vitals == null || !vitals.IsDown || vitals.IsDead) continue;

                int left = Mathf.CeilToInt(vitals.BleedOutRemaining);
                string line = vitals.Captivity switch
                {
                    Captivity.Dragged => $"Your partner is being dragged away - stop it - {left}s",
                    Captivity.Caged => $"Your partner is caged - break the lock - {left}s",
                    _ => $"Your partner is down - {left}s"
                };

                GUI.Label(new Rect(24f, 76f, 700f, 30f), line, textStyle);
                return;
            }
        }

        private static readonly Color Gold = new Color(1f, 0.78f, 0.36f);

        // Keys, pages, the banishment and the exit bolts, top left - and the
        // big warnings across the top when the demon goes or comes back.
        private void DrawRun(SpawnDirector director)
        {
            RunSnapshot run = director.State;

            string keys = director.AllKeysHeld
                ? "All keys found"
                : $"Keys {run.keysHeld} / {director.KeyCount}  -  find the {director.KeyName(run.keysHeld)}";
            GUI.Label(new Rect(24f, 20f, 700f, 30f), keys, textStyle);

            GUIStyle pages = new GUIStyle(textStyle);
            pages.normal.textColor = run.pagesHeld > 0 ? Gold : textStyle.normal.textColor;
            string pageLine = run.pagesHeld > 0
                ? $"Rite pages {run.pagesHeld} / {RunSnapshot.PageSlots}  -  hold R to banish it ({director.BanishFor(run.pagesHeld):0}s)"
                : $"Rite pages 0 / {RunSnapshot.PageSlots}  -  listen for the whispering";
            GUI.Label(new Rect(24f, 104f, 760f, 30f), pageLine, pages);

            if (director.AllKeysHeld && Deadbolt.Count > 0 && run.boltsOpen < Deadbolt.Count)
                GUI.Label(new Rect(24f, 132f, 760f, 30f),
                    $"Exit bolts {run.boltsOpen} / {Deadbolt.Count}  -  every one is loud. Keep watch.", textStyle);

            if (run.banished)
            {
                GUIStyle banished = new GUIStyle(centerStyle) { fontSize = 26 };
                banished.normal.textColor = Gold;
                GUI.Label(new Rect(0f, 24f, Screen.width, 40f),
                    $"BANISHED  -  {Mathf.CeilToInt(director.BanishRemaining)}s", banished);
            }
            else if (Time.time - director.LastReturnTime < 4f)
            {
                GUIStyle back = new GUIStyle(centerStyle) { fontSize = 26 };
                back.normal.textColor = new Color(0.8f, 0.1f, 0.1f);
                GUI.Label(new Rect(0f, 24f, Screen.width, 40f), "IT HAS RETURNED  -  FASTER", back);
            }
        }

        // While R is held: the rite itself, for the player to read aloud, each
        // line lighting up as the reading reaches it. And for a few seconds
        // after a page is found, that page's verse.
        private void DrawChant(float cx, float cy)
        {
            SpawnDirector director = SpawnDirector.Instance;
            PlayerController local = PlayerRegistry.Local;
            RiteCaster caster = local != null ? local.GetComponent<RiteCaster>() : null;

            if (director != null && caster != null && (caster.Reading || caster.Progress > 0f))
            {
                DrawRite(director.State.pagesHeld, caster.Progress, cx);
                return;
            }

            if (director != null && Time.time - director.LastPageTime < 6f)
                DrawFoundPage(director.LastPageVerse, Time.time - director.LastPageTime, cx);
        }

        private void DrawRite(int pages, float progress, float cx)
        {
            string[] lines = RiteText.Lines(pages);
            float top = Screen.height * 0.16f;

            GUIStyle title = new GUIStyle(centerStyle) { fontSize = 20 };
            title.normal.textColor = new Color(Gold.r, Gold.g, Gold.b, 0.8f);
            GUI.Label(new Rect(0f, top, Screen.width, 30f), RiteText.Title + "  -  read it aloud", title);

            GUIStyle line = new GUIStyle(centerStyle) { fontSize = 24, wordWrap = true };
            for (int i = 0; i < lines.Length; i++)
            {
                // Lit once the reading reaches it; the next one glimmers ahead.
                float reached = progress * lines.Length - i;
                float alpha = reached >= 0f ? 1f : Mathf.Clamp01(1f + reached) * 0.35f + 0.12f;
                line.normal.textColor = new Color(Gold.r, Gold.g, Gold.b, alpha);
                GUI.Label(new Rect(Screen.width * 0.1f, top + 40f + i * 36f, Screen.width * 0.8f, 34f), lines[i], line);
            }

            DrawBar(new Rect(cx - 140f, top + 50f + lines.Length * 36f, 280f, 6f), progress, Gold);
        }

        private void DrawFoundPage(int verse, float age, float cx)
        {
            float alpha = Mathf.Clamp01(Mathf.Min(age * 3f, (6f - age) * 1.5f));
            float top = Screen.height * 0.2f;

            GUIStyle title = new GUIStyle(centerStyle) { fontSize = 20 };
            title.normal.textColor = new Color(Gold.r, Gold.g, Gold.b, alpha * 0.8f);
            GUI.Label(new Rect(0f, top, Screen.width, 30f),
                $"PAGE {verse + 1} OF THE RITE  -  hold R to read what you have", title);

            GUIStyle words = new GUIStyle(centerStyle) { fontSize = 22, wordWrap = true };
            words.normal.textColor = new Color(Gold.r, Gold.g, Gold.b, alpha);
            GUI.Label(new Rect(Screen.width * 0.1f, top + 36f, Screen.width * 0.8f, 80f), RiteText.Verse(verse), words);
        }

        // While hidden: the controls, your breath, and whether it is close.
        private void DrawHiding()
        {
            PlayerController local = PlayerRegistry.Local;
            if (local == null || !local.IsConfined) return;

            HidingBreath breath = local.GetComponent<HidingBreath>();
            float bottom = Screen.height - 110f;

            GUIStyle hint = new GUIStyle(textStyle) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0f, bottom, Screen.width, 26f), "SPACE hold breath  -  Q peek  -  E step out", hint);

            if (breath != null)
            {
                Color colour = breath.Winded ? new Color(0.8f, 0.2f, 0.15f) : new Color(0.55f, 0.7f, 0.8f);
                DrawBar(new Rect(Screen.width * 0.5f - 100f, bottom + 30f, 200f, 6f), breath.Breath, colour);
            }

            // Close enough to hear you breathe. Guests see this too: the
            // stalker's body is synced even though it thinks on the host.
            foreach (EnemyStalkerAI demon in FindObjectsByType<EnemyStalkerAI>(FindObjectsSortMode.None))
            {
                if (demon.IsBanished) continue;
                if (Vector3.Distance(demon.transform.position, local.transform.position) > demon.SniffRange * 1.4f) continue;

                GUIStyle warn = new GUIStyle(centerStyle) { fontSize = 24 };
                warn.normal.textColor = new Color(0.8f, 0.12f, 0.1f);
                GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 36f),
                    local.HoldingBreath ? "don't breathe" : "it's right outside", warn);
                break;
            }
        }

        // A quiet name over your teammate's head - only when you could see
        // them anyway (in view, in range, nothing solid between), so it helps
        // find each other in the dark without seeing through walls.
        private void DrawTeammates()
        {
            PlayerController local = PlayerRegistry.Local;
            Camera view = local != null ? local.GetComponentInChildren<Camera>() : null;
            if (view == null) return;

            foreach (PlayerController other in PlayerRegistry.All)
            {
                if (other == null || other == local || other.IsLocalPlayer) continue;

                Vector3 head = other.transform.position + Vector3.up * 2.1f;
                float distance = Vector3.Distance(view.transform.position, head);
                if (distance > 25f) continue;

                Vector3 screen = view.WorldToScreenPoint(head);
                if (screen.z <= 0f) continue;

                if (Physics.Linecast(view.transform.position, head, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(other.transform)
                    && !hit.collider.transform.IsChildOf(local.transform))
                    continue;

                string label = string.IsNullOrEmpty(other.DisplayName) ? "partner" : other.DisplayName;
                PlayerVitals vitals = other.GetComponent<PlayerVitals>();
                if (vitals != null && vitals.IsDown && !vitals.IsDead) label += "  (down)";

                GUIStyle tag = new GUIStyle(textStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                tag.normal.textColor = new Color(0.85f, 0.83f, 0.78f, Mathf.Lerp(0.85f, 0.25f, distance / 25f));
                GUI.Label(new Rect(screen.x - 100f, Screen.height - screen.y - 12f, 200f, 24f), label, tag);
            }
        }

        private static void DrawBar(Rect rect, float fraction, Color colour)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = colour;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void DrawHeld()
        {
            PlayerController local = PlayerRegistry.Local;
            PlayerHands hands = local != null ? local.GetComponent<PlayerHands>() : null;
            if (hands == null || hands.Held == null) return;

            GUIStyle held = new GUIStyle(textStyle) { alignment = TextAnchor.LowerRight };
            GUI.Label(new Rect(0f, Screen.height - 50f, Screen.width - 24f, 30f),
                hands.Held.DisplayName.ToUpperInvariant() + "  ·  click to use  ·  G to drop", held);
        }

        // Proximity chat, when connected: how you are transmitting, and who is
        // talking - handy when your partner is out of sight but in earshot.
        private void DrawVoiceChat()
        {
            if (!FearMe.Net.VoiceStatus.Active) return;

            string line = FearMe.Net.VoiceStatus.PushToTalk
                ? (FearMe.Net.VoiceStatus.Transmitting ? "VOICE  -  on air" : "VOICE  -  hold V to talk")
                : "VOICE  -  open mic";
            if (FearMe.Net.VoiceStatus.LocalSpeaking) line += "  -  you";
            if (FearMe.Net.VoiceStatus.PartnerSpeaking) line += "  -  partner";

            GUIStyle voice = new GUIStyle(textStyle) { fontSize = 15 };
            voice.normal.textColor = FearMe.Net.VoiceStatus.LocalSpeaking
                ? new Color(0.75f, 0.85f, 0.65f)
                : new Color(0.55f, 0.54f, 0.52f);
            GUI.Label(new Rect(24f, Screen.height - 46f, 600f, 26f), line, voice);
        }

        // Only when the microphone is on and picking you up: a warning that
        // arrives before the stalker does.
        private void DrawVoice()
        {
            float level = MicrophoneNoise.Level;
            if (level <= 0f) return;

            GUIStyle voice = new GUIStyle(textStyle) { alignment = TextAnchor.LowerCenter };
            voice.normal.textColor = Color.Lerp(new Color(0.7f, 0.68f, 0.64f, 0.6f),
                new Color(0.85f, 0.15f, 0.12f, 1f), level);

            GUI.Label(new Rect(0f, Screen.height - 70f, Screen.width, 30f),
                level > 0.6f ? "IT CAN HEAR YOU" : "your voice carries", voice);
        }

        private void OnGUI()
        {
            BuildStyles();

            if (gameFlow != null && gameFlow.IsFinished)
            {
                DrawEndCard(gameFlow.DidEscape);
                return;
            }

            if (SpawnDirector.Instance != null)
            {
                DrawRun(SpawnDirector.Instance);
            }
            else if (objectives != null)
            {
                string status = objectives.AllKeysCollected
                    ? "All keys found - get to the door"
                    : $"Keys {objectives.KeysCollected} / {objectives.KeysRequired}";
                GUI.Label(new Rect(24f, 20f, 460f, 30f), status, textStyle);
            }

            GUI.Label(new Rect(24f, 48f, 460f, 30f), "F flashlight - C crouch - Shift run", textStyle);
            DrawHeld();

            DrawTeammates();
            DrawVitals();
            DrawVoice();
            DrawVoiceChat();
            DrawHiding();

            // Crosshair
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (interactor != null && interactor.CurrentTarget != null)
            {
                GUI.Label(
                    new Rect(cx - 200f, cy + 28f, 400f, 30f),
                    interactor.CurrentTarget.Prompt,
                    new GUIStyle(textStyle) { alignment = TextAnchor.MiddleCenter });

                // Anything that takes a held effort shows how far along it is.
                float effort = interactor.CurrentTarget is Deadbolt bolt ? bolt.Progress
                    : interactor.CurrentTarget is CageSpot cage ? cage.Progress : 0f;
                if (effort > 0f)
                    DrawBar(new Rect(cx - 90f, cy + 60f, 180f, 6f), effort, new Color(0.8f, 0.75f, 0.65f));
            }

            DrawChant(cx, cy);
        }
    }
}
