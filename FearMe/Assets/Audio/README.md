# FearMe — Audio Pack

Generated with ElevenLabs `eleven_text_to_sound_v2`. Drag each folder's clips into the
matching serialized array in the Inspector.

## DiegeticSoundscape → Structure Sounds
`Audio/Diegetic/Structure/`

| Clip | Description |
| --- | --- |
| Structure_BeamCreak | Old wooden roof beam creaking under load, slow dry groan |
| Structure_FloorSettle | House settling at night, single sharp floorboard pop |
| Structure_PipeKnock | Metal water pipe knocking twice inside a wall |
| Structure_HingeStrain | Rusty door hinge straining slowly, no slam |

## DiegeticSoundscape → Movement Sounds
`Audio/Diegetic/Movement/`

| Clip | Description |
| --- | --- |
| Movement_FootstepWood | Single slow footstep on bare floorboard |
| Movement_ClothRustle | Fabric rustling as someone shifts |
| Movement_DragScrape | Heavy object dragging across dusty wood |
| Movement_StairCreak | Staircase step creaking under slow weight |
| Movement_FootstepsAbove | Muffled footsteps on the floor above |

## PlayerBreathing → Calm Breaths
`Audio/Breathing/Calm/`

| Clip | Description |
| --- | --- |
| Calm_Breath_01 | Slow nasal breathing, relaxed, two steady breaths |

## PlayerBreathing → Heavy Breaths
`Audio/Breathing/Heavy/` — **empty, not yet generated**

## AmbientAudioController → Stacked Layers
`Audio/Ambient/Layers/` — **empty, not yet generated**

Intended layer order (quietest/most constant first):
1. Sub-bass drone — steady, felt more than heard
2. Room tone — abandoned house air hiss, distant wind
3. Tension shimmer — high thin dissonant ringing
4. Dread swell — slow rise and fall, no impact hit

## Unity import settings

- One-shots (Structure, Movement, breaths): Load Type `Decompress On Load`, Force To Mono on,
  Preload Audio Data off.
- Ambient layers: Load Type `Streaming`, Compression `Vorbis`, Loop on in the AudioSource.
