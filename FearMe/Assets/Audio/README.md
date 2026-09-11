# FearMe — Generated Audio Pack

Clips generated with ElevenLabs `eleven_text_to_sound_v2`. Assign each folder's
contents to the matching serialized field in the Inspector.

## DiegeticSoundscape

`Scripts/Core/DiegeticSoundscape.cs` — spatialised one-shots played from a
voice pool at `minDistance`..`maxDistance`, pacing tightening with tension.

### → Structure Sounds (`structureSounds`) — `Audio/Diegetic/Structure/`

| Clip | Description |
| --- | --- |
| Structure_BeamCreak | Old wooden roof beam creaking under load, slow dry groan |
| Structure_FloorSettle | House settling at night, single sharp floorboard pop |
| Structure_PipeKnock | Metal water pipe knocking twice inside a wall |
| Structure_HingeStrain | Rusty door hinge straining slowly, no slam |

### → Movement Sounds (`movementSounds`) — `Audio/Diegetic/Movement/`

| Clip | Description |
| --- | --- |
| Movement_FootstepWood | Single slow footstep on bare floorboard |
| Movement_ClothRustle | Fabric rustling as someone shifts |
| Movement_DragScrape | Heavy object dragging across dusty wood |
| Movement_StairCreak | Staircase step creaking under slow weight |
| Movement_FootstepsAbove | Muffled footsteps on the floor above |

All are short and dry so the AudioSource rolloff does the placing — avoid clips
with baked-in reverb here, they fight the spatial blend.

## PlayerBreathing

`Scripts/Player/PlayerBreathing.cs` — 2D, non-spatial, picked by exertion
against `heavyThreshold`.

### → Calm Breaths (`calmBreaths`) — `Audio/Breathing/Calm/`

| Clip | Description |
| --- | --- |
| Calm_Breath_01 | Slow nasal breathing, relaxed, two steady breaths |

### → Heavy Breaths (`heavyBreaths`) — `Audio/Breathing/Heavy/`

**Empty — still to generate.** Intended set:

1. Fast panicked panting, short sharp breaths through the mouth
2. Ragged exhausted breathing after running, hoarse and uneven
3. Sharp frightened gasp then shaky suppressed breathing

## AmbientAudioController → Stacked Layers

`Scripts/Core/AmbientAudioController.cs`, field `layers` (`AmbientLayer[]`).
Each entry has its own `source`, `clip`, `startsAt` tension threshold and
`maxVolume`, so layers arrive by degrees rather than one track swelling.

`Audio/Ambient/Layers/` — **empty, still to generate.** Intended stack:

| Label | Clip to generate | startsAt | maxVolume |
| --- | --- | --- | --- |
| Sub | Deep sustained sub-bass drone, steady, felt more than heard | 0.00 | 0.45 |
| Room | Abandoned house room tone, faint air hiss and distant wind | 0.15 | 0.40 |
| Shimmer | High thin metallic shimmer, unstable dissonant ringing | 0.45 | 0.35 |
| Dread | Low dread swell rising slowly then falling away, no impact hit | 0.70 | 0.55 |

Each layer source must loop, so these need seamless clips — generate long and
crossfade the ends, or the seam reads as a tick every few seconds.

## Unity import settings

- One-shots (Structure, Movement, breaths): Load Type `Decompress On Load`,
  Force To Mono on, Preload Audio Data off.
- Ambient layers: Load Type `Streaming`, Compression `Vorbis`, Loop on the
  AudioSource.

`Tools/FearMe/Wire Ambient Audio` wires the bed and tension sources but does
not populate `layers` — those are assigned by hand.
