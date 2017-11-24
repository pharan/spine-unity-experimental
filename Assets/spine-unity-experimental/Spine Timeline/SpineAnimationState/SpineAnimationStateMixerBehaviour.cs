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

#define SPINE_EDITMODEPOSE

using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Spine.Unity.Playables {
	public class SpineAnimationStateMixerBehaviour : PlayableBehaviour {

		float[] lastInputWeights;

		// NOTE: This function is called at runtime and edit time. Keep that in mind when setting the values of properties.
		public override void ProcessFrame (Playable playable, FrameData info, object playerData) {
			var trackBinding = playerData as SkeletonAnimation;
			if (trackBinding == null) return;

			if (!Application.isPlaying) {
				#if SPINE_EDITMODEPOSE
				PreviewEditModePose(playable, trackBinding);
				#endif
				return;
			}

			int inputCount = playable.GetInputCount();

			if (this.lastInputWeights == null || this.lastInputWeights.Length < inputCount) {
				this.lastInputWeights = new float[inputCount];

				for (int i = 0; i < inputCount; i++)
					this.lastInputWeights[i] = 0f;				
			}

			var lastInputWeights = this.lastInputWeights;

			for (int i = 0; i < inputCount; i++) {
				float lastInputWeight = lastInputWeights[i];
				float inputWeight = playable.GetInputWeight(i);
				bool trackStarted = inputWeight > lastInputWeight;
				lastInputWeights[i] = inputWeight;

				if (trackStarted) {
					ScriptPlayable<SpineAnimationStateBehaviour> inputPlayable = (ScriptPlayable<SpineAnimationStateBehaviour>)playable.GetInput(i);
					SpineAnimationStateBehaviour input = inputPlayable.GetBehaviour();
					Spine.Animation animation = trackBinding.Skeleton.Data.FindAnimation(input.animationName);

					if (animation != null) {
						Spine.TrackEntry trackEntry = trackBinding.AnimationState.SetAnimation(0, input.animationName, input.loop);

						trackEntry.EventThreshold = input.eventThreshold;
						trackEntry.DrawOrderThreshold = input.drawOrderThreshold;
						trackEntry.AttachmentThreshold = input.attachmentThreshold;

						if (input.customDuration)
							trackEntry.MixDuration = input.mixDuration;
					} else if (string.IsNullOrEmpty(input.animationName)) {
						float mixDuration = input.customDuration ? input.mixDuration : trackBinding.AnimationState.Data.DefaultMix;
						trackBinding.AnimationState.SetEmptyAnimation(0, mixDuration);
						continue;
					}
//					else {
//						Debug.LogWarningFormat("Animation named '{0}' not found", input.animationName);
//					}

				}
			}
		}

		#if SPINE_EDITMODEPOSE
		public void PreviewEditModePose (Playable playable, SkeletonAnimation trackBinding) {
			if (Application.isPlaying) return;
			if (trackBinding == null) return;

			int inputCount = playable.GetInputCount();
			int lastOneWeight = -1;

			for (int i = 0; i < inputCount; i++) {
				float inputWeight = playable.GetInputWeight(i);
				if (inputWeight >= 1) lastOneWeight = i;
			}

			if (lastOneWeight != -1) {
				ScriptPlayable<SpineAnimationStateBehaviour> inputPlayableClip = (ScriptPlayable<SpineAnimationStateBehaviour>)playable.GetInput(lastOneWeight);
				SpineAnimationStateBehaviour clipBehaviourData = inputPlayableClip.GetBehaviour();

				var skeleton = trackBinding.Skeleton;
				var skeletonData = trackBinding.Skeleton.Data;

				ScriptPlayable<SpineAnimationStateBehaviour> fromClip;
				Animation fromAnimation = null;
				float fromClipTime = 0;
				bool fromClipLoop = false;
				if (lastOneWeight != 0 && inputCount > 1) {
					fromClip = (ScriptPlayable<SpineAnimationStateBehaviour>)playable.GetInput(lastOneWeight - 1);
					var fromClipData = fromClip.GetBehaviour();
					fromAnimation = skeletonData.FindAnimation(fromClipData.animationName);
					fromClipTime = (float)fromClip.GetTime();
					fromClipLoop = fromClipData.loop;
				}
					
				Animation toAnimation = skeletonData.FindAnimation(clipBehaviourData.animationName);
				float toClipTime = (float)inputPlayableClip.GetTime();
				float mixDuration = clipBehaviourData.mixDuration;
				if (!clipBehaviourData.customDuration && fromAnimation != null) {
					mixDuration = trackBinding.AnimationState.Data.GetMix(fromAnimation, toAnimation);
				}

				// Approximate what AnimationState might do at runtime.
				if (fromAnimation != null && mixDuration > 0 && toClipTime < mixDuration) {
					skeleton.SetToSetupPose();
					float fauxFromAlpha = (1f - toClipTime/mixDuration);
					fauxFromAlpha = fauxFromAlpha > 0.5f ? 1f : fauxFromAlpha * 2f;  // fake value, but reduce dip.
					fromAnimation.Apply(skeleton, 0, fromClipTime, fromClipLoop, null, fauxFromAlpha, MixPose.Setup, MixDirection.Out); //fromAnimation.PoseSkeleton(skeleton, fromClipTime, fromClipLoop);
					toAnimation.Apply(skeleton, 0, toClipTime, clipBehaviourData.loop, null, toClipTime/mixDuration, MixPose.Current, MixDirection.In);
				} else {
					skeleton.SetToSetupPose();
					toAnimation.PoseSkeleton(skeleton, toClipTime, clipBehaviourData.loop);
				}


			}
			// Do nothing outside of the first clip and the last clip.


		}
		#endif

	}

}
