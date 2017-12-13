/******************************************************************************
 * Spine Runtimes Software License v2.5
 *
 * Copyright (c) 2013-2016, Esoteric Software
 * All rights reserved.
 *
 * You are granted a perpetual, non-exclusive, non-sublicensable, and
 * non-transferable license to use, install, execute, and perform the Spine
 * Runtimes software and derivative works solely for personal or internal
 * use. Without the written permission of Esoteric Software (see Section 2 of
 * the Spine Software License Agreement), you may not (a) modify, translate,
 * adapt, or develop new applications using the Spine Runtimes or otherwise
 * create derivative works or improvements of the Spine Runtimes or (b) remove,
 * delete, alter, or obscure any trademarks or any copyright, trademark, patent,
 * or other intellectual property or proprietary rights notices on or in the
 * Software, including any copy thereof. Redistributions in binary or source
 * form must include this license and terms.
 *
 * THIS SOFTWARE IS PROVIDED BY ESOTERIC SOFTWARE "AS IS" AND ANY EXPRESS OR
 * IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO
 * EVENT SHALL ESOTERIC SOFTWARE BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES, BUSINESS INTERRUPTION, OR LOSS OF
 * USE, DATA, OR PROFITS) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
 * IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 *****************************************************************************/

using UnityEngine;
using System.Collections.Generic;

// Spine Root Motion for Spine-Unity 3.6

namespace Spine.Unity.Modules {

	/// <summary>
	/// Add this component to a Spine GameObject to replace root bone motion into Transform or RigidBody motion.
	/// Only compatible with SkeletonAnimation (or other components that implement ISkeletonComponent, ISkeletonAnimation and IAnimationStateComponent)
	/// Set SpineRootMotion.enabled to enable and disable root motion override.
	/// </summary>
	public class SpineRootMotion : MonoBehaviour {
		#region Inspector

		[SpineBone]
		[Tooltip("The bone to take the motion from.")]
		[SerializeField]
		protected string sourceBoneName = "root";

		[Tooltip("Use the X-movement of the bone.")]
		public bool useX = true;

		[Tooltip("Use the Y-movement of the bone.")]
		public bool useY = false;

		[Header("Optional")]
		[Tooltip("OPTIONAL Rigidbody2D: Set this if you want this component to apply the root motion to a Rigidbody2D. \n\nNote that animation and physics updates are not always in sync. Some jittering may result at certain framerates.")]
		public Rigidbody2D rb;
		[SpineBone]
		[SerializeField]
		[Tooltip("OPTIONAL: If you are using root motion on an immediate child of the root bone, you would add the other immediate children of the root bone (for example, IK targets).\n\nAlso see the 'Refresh Sibling Bones' context menu item. ")]
		protected List<string> siblingBoneNames = new List<string>();
		#endregion

		protected Bone bone;
		protected int boneIndex;
		/// <summary>
		/// If you are using root motion on an immediate child of the root bone, you would add the other immediate children of the root bone (for example, IK targets).</summary>
		public readonly List<Bone> siblingBones = new List<Bone>();

		ISkeletonComponent skeletonComponent;
		AnimationState state;
		[System.NonSerialized] bool useRigidBody;

		Vector2 accumulatedDisplacement;

		[ContextMenu("Refresh Sibling Bones")]
		public void RefreshSiblingBones () {
			bone = GetComponent<ISkeletonComponent>().Skeleton.FindBone(sourceBoneName);
			if (bone == null) return;
			Bone boneParent = bone.parent;

			siblingBoneNames.Clear();
			if (Application.isPlaying)
				siblingBones.Clear();

			if (boneParent != null) { // was root bone
				foreach (var b in boneParent.children) {
					if (b != bone) siblingBoneNames.Add(b.data.name);
				}

				if (Application.isPlaying) {
					foreach (var b in boneParent.children) {
						if (b != bone) siblingBones.Add(b);
					}
				}
			}
		}

		void Start () {
			skeletonComponent = GetComponent<ISkeletonComponent>();

			var s = skeletonComponent as ISkeletonAnimation;
			if (s != null) s.UpdateLocal += HandleUpdateLocal;

			var sa = skeletonComponent as IAnimationStateComponent;
			if (sa != null) this.state = sa.AnimationState;

			SetSourceBone(sourceBoneName);

			var skeleton = s.Skeleton;
			siblingBones.Clear();
			foreach (var bn in siblingBoneNames) {
				var b = skeleton.FindBone(bn);
				if (b != null) siblingBones.Add(b);
			}

			useRigidBody |= (rb != null);
		}

		void HandleUpdateLocal (ISkeletonAnimation animatedSkeletonComponent) {
			if (!this.isActiveAndEnabled) return; // Root motion is only applied when component is enabled.

			Vector2 localDelta = Vector2.zero;
			TrackEntry current = state.GetCurrent(0); // Only apply root motion using AnimationState Track 0.

			TrackEntry track = current;
			TrackEntry next = null;
			int boneIndex = this.boneIndex;

			while (track != null) {
				var a = track.Animation;
				var tt = a.FindTranslateTimelineForBone(boneIndex);

				if (tt != null) {
					// 1. Get the delta position from the root bone's timeline.
					float start = track.animationLast;
					float end = track.AnimationTime;
					Vector2 currentDelta;
					if (start > end)
						currentDelta = (tt.Evaluate(end) - tt.Evaluate(0)) + (tt.Evaluate(a.duration) - tt.Evaluate(start));  // Looped
					else if (start != end)
						currentDelta = tt.Evaluate(end) - tt.Evaluate(start);  // Non-looped
					else
						currentDelta = Vector2.zero;

					// 2. Apply alpha to the delta position (based on AnimationState.cs)
					float mix;
					if (next != null) {
						if (next.mixDuration == 0) { // Single frame mix to undo mixingFrom changes.
							mix = 1;
						} else {
							mix = next.mixTime / next.mixDuration;
							if (mix > 1) mix = 1;
						}
						float mixAndAlpha = track.alpha * next.interruptAlpha * (1 - mix);
						currentDelta *= mixAndAlpha;
					} else {						
						if (track.mixDuration == 0) {
							mix = 1;
						} else {
							mix = track.alpha * (track.mixTime / track.mixDuration);
							if (mix > 1) mix = 1;
						}
						currentDelta *= mix;
					}

					// 3. Add the delta from the track to the accumulated value.
					localDelta += currentDelta;
				}

				// Traverse mixingFrom chain.
				next = track;
				track = track.mixingFrom;
			}

			// 4. Apply flip to the delta position.
			var skeleton = animatedSkeletonComponent.Skeleton;
			if (skeleton.flipX) localDelta.x = -localDelta.x;
			if (skeleton.flipY) localDelta.y = -localDelta.y;

			// 5. Apply root motion to Transform or RigidBody;
			if (!useX) localDelta.x = 0f;
			if (!useY) localDelta.y = 0f;

			if (useRigidBody) {
				accumulatedDisplacement += (Vector2)transform.TransformVector(localDelta);
				// Accumulated displacement is applied on the next Physics update (FixedUpdate)
			} else {
				transform.Translate(localDelta, Space.Self);
			}

			// 6. Position bones to be base position
			// BasePosition = new Vector2(0, 0);
			foreach (var b in siblingBones) {
				if (useX) b.x -= bone.x;
				if (useY) b.y -= bone.y;
			}

			if (useX) bone.x = 0;
			if (useY) bone.y = 0;
		}

		// If not using Rigidbody2D, you can comment out FixedUpdate.
		void FixedUpdate () {
			if (this.isActiveAndEnabled && this.useRigidBody) { // Root motion is only applied when component is enabled.
				Vector2 v = rb.velocity;
				if (useX) v.x = accumulatedDisplacement.x / Time.fixedDeltaTime;
				if (useY) v.y = accumulatedDisplacement.y / Time.fixedDeltaTime;
				rb.velocity = v;
				accumulatedDisplacement = Vector2.zero;

				// When using Transform position. This causes the rigidbody to lose contact data.
//				var p = transform.position;
//				if (controlRigidbodyX) p.x += accumulatedDisplacement.x;
//				if (controlRigidbodyY) p.y += accumulatedDisplacement.y;
//				transform.position = p;
//				accumulatedDisplacement = Vector2.zero;
			}
		}

		public void SetSourceBone (string name) {
			var skeleton = skeletonComponent.Skeleton;
			int bi = skeleton.FindBoneIndex(name);
			if (bi >= 0) {
				this.boneIndex = bi;
				this.bone = skeleton.bones.Items[bi];
			} else {
				Debug.Log("Bone named \"" + name + "\" could not be found.");
				this.boneIndex = 0;
				this.bone = skeleton.RootBone;
			}
		}

		public static Bone GetRootBranchOf (Bone b) {
			if (b == null) return null;
			Bone rootBone = b.skeleton.RootBone;
			if (b.parent == null) return null; // was root bone or invalid.
			if (b.parent == rootBone) return b;

			const int BoneSearchLimit = 500;
			for (int i = 0; i < BoneSearchLimit; i++) {
				Bone parent = b.parent;
				if (parent == rootBone) return b;
				b = parent;
			}

			return null;
		}

		void OnDisable () {
			accumulatedDisplacement = Vector2.zero;
		}

	}

	public static class TimelineTools {
		/// <summary>Gets the translate timeline for a given boneIndex. You can get the boneIndex using SkeletonData.FindBoneIndex. The root bone is always boneIndex 0.
		/// This will return null if a TranslateTimeline is not found.</summary>
		public static TranslateTimeline FindTranslateTimelineForBone (this Animation a, int boneIndex) {
			foreach (var t in a.timelines) {
				var tt = t as TranslateTimeline;
				if (tt != null && tt.boneIndex == boneIndex)
					return tt;
			}
			return null;
		}

		/// <summary>Evaluates the resulting value of a TranslateTimeline at a given time.
		/// SkeletonData can be accessed from Skeleton.Data or from SkeletonDataAsset.GetSkeletonData.
		/// If no SkeletonData is given, values are computed relative to setup pose instead of local-absolute.</summary>
		public static Vector2 Evaluate (this TranslateTimeline tt, float time, SkeletonData skeletonData = null) {
			const int PREV_TIME = -3, PREV_X = -2, PREV_Y = -1;
			const int X = 1, Y = 2;

			var frames = tt.frames;
			if (time < frames[0]) return Vector2.zero;

			float x, y;
			if (time >= frames[frames.Length - TranslateTimeline.ENTRIES]) { // Time is after last frame.
				x = frames[frames.Length + PREV_X];
				y = frames[frames.Length + PREV_Y];
			} else {
				int frame = Animation.BinarySearch(frames, time, TranslateTimeline.ENTRIES);
				x = frames[frame + PREV_X];
				y = frames[frame + PREV_Y];
				float frameTime = frames[frame];
				float percent = tt.GetCurvePercent(frame / TranslateTimeline.ENTRIES - 1, 1 - (time - frameTime) / (frames[frame + PREV_TIME] - frameTime));

				x += (frames[frame + X] - x) * percent;
				y += (frames[frame + Y] - y) * percent;
			}

			Vector2 o = new Vector2(x, y);

			if (skeletonData == null) {
				return o;
			} else {
				var boneData = skeletonData.bones.Items[tt.boneIndex];
				return o + new Vector2(boneData.x, boneData.y);
			}
		}
	}
}