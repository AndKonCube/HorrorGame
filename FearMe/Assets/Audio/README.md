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

| Clip | Description | Source |
| --- | --- | --- |
| Calm_Breath_01 | Slow nasal breathing, relaxed, two steady breaths | ElevenLabs |
| Calm_Breath_02 | Soft exhale through parted lips | synthesised |
| Calm_Breath_03 | Slow deep breath in, pause, long breath out | synthesised |

### → Heavy Breaths (`heavyBreaths`) — `Audio/Breathing/Heavy/`

| Clip | Description | Source |
| --- | --- | --- |
| Heavy_Breath_01 | Fast panicked panting, five shallow mouth breaths | synthesised |
| Heavy_Breath_02 | Ragged and exhausted, voiced rasp on the exhales | synthesised |
| Heavy_Breath_03 | Sharp gasp, then breathing held down small and tight | synthesised |

## AmbientAudioController → Stacked Layers

`Scripts/Core/AmbientAudioController.cs`, field `layers` (`AmbientLayer[]`).
Each entry has its own `source`, `clip`, `startsAt` tension threshold and
`maxVolume`, so layers arrive by degrees rather than one track swelling.

`Audio/Ambient/Layers/` — four 20 s clips, all synthesised and loop-clean.

| Label | Clip | startsAt | maxVolume |
| --- | --- | --- | --- |
| Sub | Layer_1_Sub — 36/54 Hz drone, felt more than heard | 0.00 | 0.45 |
| Room | Layer_2_Room — air, rumble, wind swelling three times per loop | 0.15 | 0.40 |
| Shimmer | Layer_3_Shimmer — close dissonant partials beating at 2.3-3.1 kHz | 0.45 | 0.35 |
| Dread | Layer_4_Dread — slow swell rising and falling away, no impact hit | 0.70 | 0.55 |

These loop seamlessly by construction, not by luck: every tonal component
completes a whole number of cycles across the 20 s, and every noise bed is
filtered first and then wrapped with a circular crossfade. Measured wrap
discontinuity is at or below ordinary sample-to-sample motion on all four.

Layer 1 is genuinely sub-bass — it will be close to inaudible on laptop
speakers and carry the whole scene on headphones. Judge its level on
headphones, not on a monitor speaker.

## Regenerating the synthesised clips

    python3 Tools/audiogen/synth_audio.py FearMe/Assets/Audio

Pure standard library, no install step. The random seed is fixed, so a re-run
reproduces the same clips; change the seed in `main()` for fresh variants, or
edit the per-clip parameters to retune any one of them.

## Unity import settings

- One-shots (Structure, Movement, breaths): Load Type `Decompress On Load`,
  Force To Mono on, Preload Audio Data off.
- Ambient layers: Load Type `Streaming`, Compression `Vorbis`, Loop on the
  AudioSource. Leave the WAVs as WAV — re-encoding a seamless loop to MP3 adds
  encoder padding and reintroduces the gap at the wrap.

`Tools/FearMe/Wire Ambient Audio` wires the bed and tension sources but does
not populate `layers` — those are assigned by hand.
