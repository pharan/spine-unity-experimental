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

using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Spine.Unity;
using Spine;
using System.Collections.Generic;

namespace Spine.Unity.Playables {
	public class SpineAnimationMixerBehaviour : PlayableBehaviour {
		SpinePlayableHandleBase trackBindingPlayableHandle;
		bool clipsInitialized = false;

		public override void ProcessFrame (Playable playable, FrameData info, object playerData) {
			// Q: Does ProcessFrame come before or after MonoBehaviour.Update?
			//Debug.Log("MixerBehaviour ProcessFrame " + Time.frameCount);
			// A: MonoBehaviour.Update comes first. Then PlayableBehaviour.ProcessFrame.

			trackBindingPlayableHandle = playerData as SpinePlayableHandleBase;
			if (trackBindingPlayableHandle == null) return;

			// Ensure initialize all clipBehaviourData.
			if (!clipsInitialized) {
				var skeletonData = trackBindingPlayableHandle.SkeletonData;
				for (int i = 0, inputCount = playable.GetInputCount(); i < inputCount; i++) {
					var inputPlayableClip = (ScriptPlayable<SpineAnimationBehaviour>) playable.GetInput(i); // The clip. Returns a handle struct.
					SpineAnimationBehaviour clipBehaviourData = inputPlayableClip.GetBehaviour(); // the stateless data
					clipBehaviourData.EnsureInitialize(skeletonData);
				}
				clipsInitialized = true;
			}

			trackBindingPlayableHandle.ProcessFrame(playable, info, this);

			if (Application.isPlaying) {
				trackBindingPlayableHandle.HandleEvents(eventBuffer);
				eventBuffer.Clear();
			}
		}

		// TODO: Move these to the playablehandle so tracks can be aware of what the previous tracks had already applied.
		readonly ExposedList<Event> eventBuffer = new ExposedList<Event>();
		float[] lastInputWeights;
		float[] lastTimes;

		/// <summary>Applies the Playable ScriptPlayable(SpineAnimationBehaviour) to a skeleton.</summary>
		/// <returns>The number of actual applied clips (inputs with weight greater than 0) in the current frame.</returns>
		internal int ApplyPlayableFrame (Playable playable, Skeleton skeleton, HashSet<int> frameAppliedProperties, int trackIndex) {
			int inputCount = playable.GetInputCount();
			bool isUpperTrack = trackIndex > 0;

			// Prepare lastTimes and lastInputWeights array
			if (this.lastTimes == null || this.lastTimes.Length < inputCount) {
				this.lastInputWeights = new float[inputCount];
				this.lastTimes = new float[inputCount];

				for (int i = 0; i < inputCount; i++) {
					this.lastInputWeights[i] = 0f;
					this.lastTimes[i] = 0f;
				}
			}

			var lastInputWeights = this.lastInputWeights;
			var lastTimes = this.lastTimes;
			//var frameAppliedProperties = this.frameAppliedProperties;

			int currentInputs = 0;
			//frameAppliedProperties.Clear();

			// foreach (clip)
			var eventBuffer = this.eventBuffer;
			for (int i = 0; i < inputCount; i++) {
				float inputWeight = playable.GetInputWeight(i);
				var inputPlayableClip = (ScriptPlayable<SpineAnimationBehaviour>) playable.GetInput(i); // The clip. Returns a handle struct.
				SpineAnimationBehaviour clipBehaviourData = inputPlayableClip.GetBehaviour(); // the stateless data

				float clipTime = (float)inputPlayableClip.GetTime(); // stateful: clip time.
				float applyTime = clipTime;
				float clipLastTime = lastTimes[i];
				//bool backwardsPlayback = clipLastTime > applyTime;

				Animation spineAnimation = clipBehaviourData.animation;
				bool loop = clipBehaviourData.loop;
				var clipEventBuffer = inputWeight > clipBehaviourData.eventThreshold ? eventBuffer : null;
				bool skipAttachments = inputWeight < clipBehaviourData.attachmentThreshold;
				bool skipDrawOrder = inputWeight < clipBehaviourData.drawOrderThreshold;


				if (spineAnimation != null) {
					//Debug.LogFormat("{0} - {1}", i, animation.name);

					if (Mathf.Approximately(inputWeight, 0)) {
						if (lastInputWeights[i] > 0) {
							if (isUpperTrack) {
								foreach (var spineTimeline in spineAnimation.timelines) {
									if (!frameAppliedProperties.Contains(spineTimeline.PropertyId))
										spineTimeline.Apply(skeleton, 0, 0, null, 0, MixPose.Setup, MixDirection.Out);
									frameAppliedProperties.Add(spineTimeline.PropertyId);
								}
								//Debug.Log("conditionally remove " + spineAnimation.name);
							} else {
								spineAnimation.SetKeyedItemsToSetupPose(skeleton); // Animation last apply.
								//Debug.Log("setkeyeditemstosetuppose " + spineAnimation.name);
							}
								
							inputWeight = 0f;
						}
						applyTime = lastTimes[i];
						// Don't do else if input weight is <= 0. This is part of the Unity reference implementation.

					} else {
//						if (isUpperTrack) {
//							Debug.Log(trackIndex + " applying " + spineAnimation.name + " " + Time.frameCount + " " + inputWeight);
//						}
							
						float duration = spineAnimation.duration;
						if (loop && duration != 0) {
							applyTime %= duration;
							if (clipLastTime > 0) clipLastTime %= duration;
						}

						// EXPERIMENTAL: Allow first animation to mix-in rather than do a flat Setup Pose override.
						bool isFirstOnLowestTrack = !isUpperTrack && i == 0 && (inputCount == 1 || playable.GetInputWeight(1) == 0);
						MixPose animationPose = MixPose.Setup;
						MixPose trackCurrentMixType = isUpperTrack ? MixPose.CurrentLayered : MixPose.Current;

						//Animation.Apply();
						foreach (var spineTimeline in spineAnimation.Timelines) {
							int pid = spineTimeline.PropertyId;

							MixPose pose = animationPose;
							if (isFirstOnLowestTrack) {
								pose = MixPose.CurrentLayered;
							} else if (currentInputs > 0 || isUpperTrack) {
								if (frameAppliedProperties.Contains(pid))
									pose = trackCurrentMixType;
							}

							MixDirection direction = MixDirection.In;
							if (inputWeight < lastInputWeights[i]) direction = MixDirection.Out;

							if (direction == MixDirection.In) {
								if (skipAttachments && spineTimeline is AttachmentTimeline) continue;
								if (skipDrawOrder && spineTimeline is DrawOrderTimeline) continue;
							} else {
								if (skipAttachments && spineTimeline is AttachmentTimeline) {
									pose = MixPose.Setup;
									direction = MixDirection.Out;
								} 
								if (skipDrawOrder && spineTimeline is DrawOrderTimeline) {
									pose = MixPose.Setup;
									direction = MixDirection.Out;
								}
							}

//							var rot = spineTimeline as RotateTimeline;
//							if (rot != null) {
//								var bone = skeleton.bones.Items[rot.boneIndex];
//								if (bone.data.name == "rear-upper-arm") {
//									Debug.Log(bone.rotation + " " + pose);
//									Debug.Log(frameAppliedProperties.Contains(pid));
//								}
//							}

							// TODO: Handle RotateTimeline like AnimationState.
							spineTimeline.Apply(skeleton, lastTimes[i], applyTime, clipEventBuffer, inputWeight, pose, direction);
							frameAppliedProperties.Add(spineTimeline.PropertyId);
						}

						//Debug.LogFormat("Applying {0} at {1} as input [{2}] using {3} {4}", animation.Name, inputWeight, i, mixPose, mixDirection);

						currentInputs++;
					}

				}

				lastInputWeights[i] = inputWeight;
				lastTimes[i] = applyTime;
			}

			return currentInputs;
		} 

	}

}
