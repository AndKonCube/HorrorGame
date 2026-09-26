using FearMe.Core;
using UnityEngine;

namespace FearMe.Player
{
    // How a teammate looks to you: a figure that walks and runs with its arms
    // swinging, crouches, looks up and down where they are looking, goes down
    // when caught, carries what they picked up - in one hand, or both arms
    // round something heavy - and points its torch where they point theirs.
    //
    // All of it is driven in code, so it needs no animation files. It works
    // on the built-in jointed figure, or on any humanoid character model
    // (a Mixamo one, say), whose skeleton it drives the same way.
    //
    // Only ever built for the other player's body; you never see your own.
    public class TeammateBody : MonoBehaviour
    {
        private enum Bone
        {
            Hips, Spine, Head,
            LUpperArm, LLowerArm, LHand,
            RUpperArm, RLowerArm, RHand,
            LUpperLeg, LLowerLeg, RUpperLeg, RLowerLeg,
            Count
        }

        private const float WalkSpeed = 3.2f;
        private const float RunSpeed = 5.8f;

        private readonly Transform[] bones = new Transform[(int)Bone.Count];
        private readonly Quaternion[] rest = new Quaternion[(int)Bone.Count];

        private Transform figure;
        private Vector3 figureRestPosition;
        private Vector3 hipsRest;
        private Quaternion hangLArm, hangRArm, hangLLeg, hangRLeg;
        private Vector3 flexLArm, flexRArm, flexLLeg, flexRLeg;
        private bool rigged;

        private PlayerController player;
        private PlayerVitals vitals;
        private Vector3 lastPosition;
        private float speed;
        private float phase;
        private float crouch, down, carry, twoHands, torch;

        // Set by the network every frame: what the owner is doing.
        public bool Crouching { get; set; }
        public float LookPitch { get; set; }
        public bool TorchOn { get; set; }

        // Unscaled places to hang things from, moved to the hands each frame.
        public Transform HandMount { get; private set; }
        public Transform ChestMount { get; private set; }
        public Transform TorchMount { get; private set; }

        // --- Building ------------------------------------------------------------

        // A plain jointed figure, about 1.8m, made of simple shapes.
        public static TeammateBody BuildMannequin(PlayerController owner, Material skin)
        {
            Transform root = new GameObject("Mannequin").transform;
            root.SetParent(owner.transform, false);

            TeammateBody body = owner.gameObject.AddComponent<TeammateBody>();
            body.figure = root;

            Transform hips = Joint("Hips", root, new Vector3(0f, 0.95f, 0f));
            Part(PrimitiveType.Cube, hips, new Vector3(0f, 0f, 0f), new Vector3(0.32f, 0.16f, 0.2f), skin);

            Transform spine = Joint("Spine", hips, new Vector3(0f, 0.08f, 0f));
            Part(PrimitiveType.Cube, spine, new Vector3(0f, 0.27f, 0f), new Vector3(0.38f, 0.48f, 0.22f), skin);

            Transform head = Joint("Head", spine, new Vector3(0f, 0.53f, 0f));
            Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.13f, 0.01f), new Vector3(0.22f, 0.26f, 0.24f), skin);

            body.bones[(int)Bone.Hips] = hips;
            body.bones[(int)Bone.Spine] = spine;
            body.bones[(int)Bone.Head] = head;

            BuildArm(body, spine, -1f, skin, Bone.LUpperArm, Bone.LLowerArm, Bone.LHand);
            BuildArm(body, spine, 1f, skin, Bone.RUpperArm, Bone.RLowerArm, Bone.RHand);
            BuildLeg(body, hips, -1f, skin, Bone.LUpperLeg, Bone.LLowerLeg);
            BuildLeg(body, hips, 1f, skin, Bone.RUpperLeg, Bone.RLowerLeg);

            body.Initialise(owner);
            return body;
        }

        private static void BuildArm(TeammateBody body, Transform spine, float side, Material skin,
            Bone upper, Bone lower, Bone hand)
        {
            Transform shoulder = Joint(side < 0 ? "LeftArm" : "RightArm", spine, new Vector3(0.24f * side, 0.46f, 0f));
            Part(PrimitiveType.Capsule, shoulder, new Vector3(0f, -0.15f, 0f), new Vector3(0.1f, 0.16f, 0.1f), skin);

            Transform elbow = Joint("Elbow", shoulder, new Vector3(0f, -0.3f, 0f));
            Part(PrimitiveType.Capsule, elbow, new Vector3(0f, -0.13f, 0f), new Vector3(0.085f, 0.14f, 0.085f), skin);

            Transform wrist = Joint("Hand", elbow, new Vector3(0f, -0.27f, 0f));
            Part(PrimitiveType.Cube, wrist, new Vector3(0f, -0.04f, 0f), new Vector3(0.07f, 0.1f, 0.04f), skin);

            body.bones[(int)upper] = shoulder;
            body.bones[(int)lower] = elbow;
            body.bones[(int)hand] = wrist;
        }

        private static void BuildLeg(TeammateBody body, Transform hips, float side, Material skin, Bone upper, Bone lower)
        {
            Transform hip = Joint(side < 0 ? "LeftLeg" : "RightLeg", hips, new Vector3(0.1f * side, -0.04f, 0f));
            Part(PrimitiveType.Capsule, hip, new Vector3(0f, -0.22f, 0f), new Vector3(0.14f, 0.23f, 0.14f), skin);

            Transform knee = Joint("Knee", hip, new Vector3(0f, -0.44f, 0f));
            Part(PrimitiveType.Capsule, knee, new Vector3(0f, -0.21f, 0f), new Vector3(0.11f, 0.22f, 0.11f), skin);
            Part(PrimitiveType.Cube, knee, new Vector3(0f, -0.44f, 0.05f), new Vector3(0.1f, 0.06f, 0.24f), skin);

            body.bones[(int)upper] = hip;
            body.bones[(int)lower] = knee;
        }

        private static Transform Joint(string name, Transform parent, Vector3 local)
        {
            Transform joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.localPosition = local;
            return joint;
        }

        private static void Part(PrimitiveType type, Transform parent, Vector3 local, Vector3 scale, Material skin)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = local;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = skin;
        }

        // A character model. A humanoid one gets the full treatment through
        // its skeleton; anything else still moves, carries and lights up, it
        // just does not swing its limbs.
        public static TeammateBody FromModel(PlayerController owner, Transform model)
        {
            TeammateBody body = owner.gameObject.AddComponent<TeammateBody>();
            body.figure = model;

            Animator animator = model.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                body.bones[(int)Bone.Hips] = animator.GetBoneTransform(HumanBodyBones.Hips);
                body.bones[(int)Bone.Spine] = animator.GetBoneTransform(HumanBodyBones.Chest) ??
                                              animator.GetBoneTransform(HumanBodyBones.Spine);
                body.bones[(int)Bone.Head] = animator.GetBoneTransform(HumanBodyBones.Head);
                body.bones[(int)Bone.LUpperArm] = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                body.bones[(int)Bone.LLowerArm] = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                body.bones[(int)Bone.LHand] = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                body.bones[(int)Bone.RUpperArm] = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                body.bones[(int)Bone.RLowerArm] = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                body.bones[(int)Bone.RHand] = animator.GetBoneTransform(HumanBodyBones.RightHand);
                body.bones[(int)Bone.LUpperLeg] = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                body.bones[(int)Bone.LLowerLeg] = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                body.bones[(int)Bone.RUpperLeg] = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                body.bones[(int)Bone.RLowerLeg] = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);

                // This drives the bones now; an animator would fight it.
                animator.enabled = false;
            }
            else if (animator != null)
            {
                Debug.Log("[FearMe] The teammate character model is not rigged as Humanoid, so it will not swing " +
                    "its limbs. Set Rig > Animation Type to Humanoid on the model to fix that.");
            }

            body.Initialise(owner);
            return body;
        }

        private void Initialise(PlayerController owner)
        {
            player = owner;
            vitals = owner.GetComponent<PlayerVitals>();
            figureRestPosition = figure.localPosition;
            lastPosition = transform.position;

            rigged = bones[(int)Bone.Hips] != null && bones[(int)Bone.LUpperLeg] != null &&
                     bones[(int)Bone.RUpperLeg] != null && bones[(int)Bone.LUpperArm] != null &&
                     bones[(int)Bone.RUpperArm] != null;

            if (rigged)
            {
                for (int i = 0; i < (int)Bone.Count; i++)
                    if (bones[i] != null) rest[i] = Quaternion.Inverse(figure.rotation) * bones[i].rotation;

                hipsRest = figure.InverseTransformPoint(bones[(int)Bone.Hips].position);

                // However the model was posed (T-pose, A-pose, arms down),
                // this is the turn that lets its limbs hang naturally.
                hangLArm = Hang(Bone.LUpperArm, Bone.LLowerArm, new Vector3(-0.12f, -1f, 0f));
                hangRArm = Hang(Bone.RUpperArm, Bone.RLowerArm, new Vector3(0.12f, -1f, 0f));
                hangLLeg = Hang(Bone.LUpperLeg, Bone.LLowerLeg, Vector3.down);
                hangRLeg = Hang(Bone.RUpperLeg, Bone.RLowerLeg, Vector3.down);

                flexLArm = Flex(Bone.LUpperArm, Bone.LLowerArm);
                flexRArm = Flex(Bone.RUpperArm, Bone.RLowerArm);
                flexLLeg = Flex(Bone.LUpperLeg, Bone.LLowerLeg);
                flexRLeg = Flex(Bone.RUpperLeg, Bone.RLowerLeg);
            }

            HandMount = Mount("HandMount", new Vector3(0.3f, 1.0f, 0.35f));
            ChestMount = Mount("ChestMount", new Vector3(0f, 1.05f, 0.38f));
            TorchMount = Mount("TorchMount", new Vector3(0.3f, 1.3f, 0.4f));
        }

        // Plain, unscaled children of the player, so whatever hangs from them
        // keeps its own size however big the model was made.
        private Transform Mount(string name, Vector3 local)
        {
            Transform mount = new GameObject(name).transform;
            mount.SetParent(transform, false);
            mount.localPosition = local;
            return mount;
        }

        private Vector3 RestDirection(Bone from, Bone to)
        {
            Transform a = bones[(int)from];
            Transform b = bones[(int)to];
            if (a == null || b == null) return Vector3.down;
            return figure.InverseTransformDirection(b.position - a.position).normalized;
        }

        private Quaternion Hang(Bone from, Bone to, Vector3 target) =>
            Quaternion.FromToRotation(RestDirection(from, to), target.normalized);

        // The axis a limb bends forward around, in its rest pose.
        private Vector3 Flex(Bone from, Bone to)
        {
            Vector3 axis = Vector3.Cross(Vector3.forward, RestDirection(from, to));
            return axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.right;
        }

        // --- Moving ----------------------------------------------------------------

        private void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            Vector3 moved = transform.position - lastPosition;
            lastPosition = transform.position;
            moved.y = 0f;
            float measured = Mathf.Min(moved.magnitude / dt, 12f);
            speed = Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-8f * dt));

            bool isDown = vitals != null && vitals.IsDown;
            HeldItem held = player != null ? player.RemoteHeldItem : null;
            bool heavy = held != null && held.TwoHanded;

            float blend = 1f - Mathf.Exp(-8f * dt);
            crouch = Mathf.Lerp(crouch, Crouching && !isDown ? 1f : 0f, blend);
            down = Mathf.Lerp(down, isDown ? 1f : 0f, blend);
            carry = Mathf.Lerp(carry, held != null && !heavy ? 1f : 0f, blend);
            twoHands = Mathf.Lerp(twoHands, heavy ? 1f : 0f, blend);
            torch = Mathf.Lerp(torch, TorchOn && !heavy ? 1f : 0f, blend);

            PoseFigure();
            if (rigged) PoseLimbs(dt);
            PlaceMounts();
        }

        // Down means on the floor: the whole figure tips back and lies there,
        // including while being dragged.
        private void PoseFigure()
        {
            figure.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-82f, 0f, 0f), down);
            figure.localPosition = figureRestPosition + Vector3.up * (0.14f * down);
        }

        private void PoseLimbs(float dt)
        {
            float moving = Mathf.Clamp01(speed / 1.2f) * (1f - down);
            float run = Mathf.InverseLerp(WalkSpeed + 0.8f, RunSpeed, speed) * (1f - crouch);

            float stride = Mathf.Lerp(1.4f, 2.3f, run) * Mathf.Lerp(1f, 0.7f, crouch);
            phase = (phase + speed * dt / stride * Mathf.PI * 2f) % (Mathf.PI * 2f);

            float legSwing = moving * Mathf.Lerp(26f, 40f, run) * Mathf.Lerp(1f, 0.6f, crouch);
            float kneeSwing = moving * Mathf.Lerp(30f, 65f, run);

            float thighL = Mathf.Sin(phase) * legSwing + crouch * 55f;
            float thighR = -Mathf.Sin(phase) * legSwing + crouch * 55f;
            float kneeL = Mathf.Max(0f, Mathf.Cos(phase)) * kneeSwing + crouch * 95f;
            float kneeR = Mathf.Max(0f, -Mathf.Cos(phase)) * kneeSwing + crouch * 95f;

            // Arms swing against the legs - less when there is something in them.
            float armSwing = moving * Mathf.Lerp(18f, 42f, run);
            float armL = -Mathf.Sin(phase) * armSwing;
            float armR = Mathf.Sin(phase) * armSwing;
            float elbowL = 12f + run * 55f;
            float elbowR = 12f + run * 55f;

            float pitch = Mathf.Clamp(LookPitch, -60f, 60f);

            // One-handed carry: right arm forward, item in the hand.
            armR = Mathf.Lerp(armR, 45f, carry);
            elbowR = Mathf.Lerp(elbowR, 55f, carry);

            // The torch: the free hand points it where they are looking.
            float aim = 80f - pitch;
            if (carry > 0.5f)
            {
                armL = Mathf.Lerp(armL, aim, torch);
                elbowL = Mathf.Lerp(elbowL, 12f, torch);
            }
            else
            {
                armR = Mathf.Lerp(armR, aim, torch);
                elbowR = Mathf.Lerp(elbowR, 12f, torch);
            }

            // Something heavy: both arms round it, hugged to the chest.
            armL = Mathf.Lerp(armL, 38f, twoHands);
            armR = Mathf.Lerp(armR, 38f, twoHands);
            elbowL = Mathf.Lerp(elbowL, 78f, twoHands);
            elbowR = Mathf.Lerp(elbowR, 78f, twoHands);

            // Lying down, the limbs just go slack.
            thighL *= 1f - down; thighR *= 1f - down;
            kneeL *= 1f - down; kneeR *= 1f - down;
            armL = Mathf.Lerp(armL, -8f, down); armR = Mathf.Lerp(armR, -8f, down);

            float lean = crouch * 25f + run * 10f + twoHands * -6f;
            float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.035f * moving - crouch * 0.29f;

            // Hips carry the bob and the crouch; everything else turns.
            Transform hips = bones[(int)Bone.Hips];
            hips.position = figure.TransformPoint(hipsRest) + figure.up * bob;

            // Spine and head point up, not down, so "forward" is the other
            // sign for them: a lean tips the chest ahead, a positive pitch
            // (looking down) drops the face.
            Set(Bone.Spine, Forward(-lean));
            Set(Bone.Head, Forward(-pitch * 0.8f));

            SetLimb(Bone.LUpperLeg, Bone.LLowerLeg, hangLLeg, flexLLeg, thighL, -kneeL);
            SetLimb(Bone.RUpperLeg, Bone.RLowerLeg, hangRLeg, flexRLeg, thighR, -kneeR);
            SetLimb(Bone.LUpperArm, Bone.LLowerArm, hangLArm, flexLArm, armL, elbowL);
            SetLimb(Bone.RUpperArm, Bone.RLowerArm, hangRArm, flexRArm, armR, elbowR);
        }

        // Positive degrees swing forward, about the figure's right axis.
        private static Quaternion Forward(float degrees) => Quaternion.AngleAxis(-degrees, Vector3.right);

        private void Set(Bone bone, Quaternion delta)
        {
            Transform t = bones[(int)bone];
            if (t != null) t.rotation = figure.rotation * delta * rest[(int)bone];
        }

        // Upper limb: hang it, then swing it. Lower limb: the same, plus its
        // own bend, worked out in the rest pose so any rig bends the right way.
        private void SetLimb(Bone upper, Bone lower, Quaternion hang, Vector3 flex, float swing, float bend)
        {
            Quaternion upperTurn = Forward(swing) * hang;
            Set(upper, upperTurn);
            Set(lower, upperTurn * Quaternion.AngleAxis(-bend, flex));
        }

        // Hands and chest, wherever the pose has put them this frame.
        private void PlaceMounts()
        {
            Quaternion facing = figure.rotation;
            Quaternion looking = transform.rotation * Quaternion.Euler(Mathf.Clamp(LookPitch, -60f, 60f), 0f, 0f);

            Transform rightHand = rigged ? bones[(int)Bone.RHand] : null;
            Transform leftHand = rigged ? bones[(int)Bone.LHand] : null;

            if (rightHand != null) HandMount.SetPositionAndRotation(rightHand.position, facing);

            // The torch lives in whichever hand is free.
            Transform torchHand = carry > 0.5f ? leftHand : rightHand;
            if (torchHand != null) TorchMount.SetPositionAndRotation(torchHand.position, looking);
            else TorchMount.localRotation = Quaternion.Euler(Mathf.Clamp(LookPitch, -60f, 60f), 0f, 0f);
        }
    }
}
