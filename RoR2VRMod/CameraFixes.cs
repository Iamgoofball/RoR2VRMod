﻿using Mono.Cecil.Cil;
using MonoMod.Cil;
using Rewired;
using RoR2;
using RoR2.Networking;
using RoR2.UI;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace VRMod
{
    internal static class CameraFixes
    {
        private static bool isTurningLeft;
        private static bool wasTurningLeft;

        private static bool isTurningRight;
        private static bool wasTurningRight;

        private static bool justTurnedLeft => isTurningLeft && !wasTurningLeft;
        private static bool justTurnedRight => isTurningRight && !wasTurningRight;

		internal static void Init()
        {
            On.RoR2.MatchCamera.Awake += (orig, self) =>
            {
                self.matchFOV = false;
                orig(self);
            };

            IL.RoR2.CameraRigController.SetCameraState += RemoveFOVChange;
            On.RoR2.CameraRigController.SetCameraState += SetCameraStateOverride;

            On.RoR2.CameraRigController.Update += CameraUpdateOverride;
			On.RoR2.CameraRigController.Start += InitCamera;

			if (ModConfig.FirstPerson.Value)
            {
                On.RoR2.Run.Update += SetBodyInvisible;

                On.RoR2.CameraRigController.OnDestroy += (orig, self) =>
                {
                    orig(self);
                    if (VRCameraWrapper.instance)
                        GameObject.Destroy(VRCameraWrapper.instance.gameObject);
                };
            }

            On.RoR2.CameraRigController.GetCrosshairRaycastRay += GetVRCrosshairRaycastRay;
        }

        private static void InitCamera(On.RoR2.CameraRigController.orig_Start orig, CameraRigController self)
        {
			orig(self);

			Transform blur = self.transform.Find("GlobalPostProcessVolume, Base");

			if (blur)
			{
				PostProcessVolume ppVolume = blur.GetComponent<PostProcessVolume>();
				if (ppVolume)
					ppVolume.profile.GetSetting<DepthOfField>().active = false;
			}

			if (Run.instance && ModConfig.ConfortVignette.Value)
            {
				self.uiCam.gameObject.AddComponent<ConfortVignette>();
			}

			if (self.hud)
				UIFixes.AdjustHUD(self.hud);
        }

        //I really didnt't want to use IL for this part... so COPYING THE WHOLE METHOD IT IS
        private static void CameraUpdateOverride(On.RoR2.CameraRigController.orig_Update orig, CameraRigController self)
        {
			if (Time.deltaTime == 0f)
			{
				return;
			}
			if (self.target != self.previousTarget)
			{
				GameObject gameObject = self.previousTarget;
				self.previousTarget = self.target;
				self.OnTargetChanged(gameObject, self.target);
			}
			self.lerpCameraTime += Time.deltaTime * self.lerpCameraTimeScale;
			self.firstPersonTarget = null;
			float num = self.baseFov;
			self.sceneCam.rect = self.viewport;
			Player player = null;
			UserProfile userProfile = null;
			bool flag = false;
			if (self.viewer && self.viewer.localUser != null)
			{
				player = self.viewer.localUser.inputPlayer;
				userProfile = self.viewer.localUser.userProfile;
				flag = self.viewer.localUser.isUIFocused;
			}
			if (self.cameraMode == CameraRigController.CameraMode.SpectateUser && player != null)
			{
				if (player.GetButtonDown(7))
				{
					self.target = CameraRigController.GetNextSpectateGameObject(self.viewer, self.target);
				}
				if (player.GetButtonDown(8))
				{
					self.target = CameraRigController.GetPreviousSpectateGameObject(self.viewer, self.target);
				}
			}
			LocalUser localUserViewer = self.localUserViewer;
			MPEventSystem mpeventSystem = (localUserViewer != null) ? localUserViewer.eventSystem : null;
			float num14;
			float num15;
			if ((!mpeventSystem || !mpeventSystem.isCursorVisible) && player != null && userProfile != null && !flag && (!(UnityEngine.Object)self.overrideCam || self.overrideCam.IsUserLookAllowed(self)))
			{
				float mouseLookSensitivity = userProfile.mouseLookSensitivity;
				float num2 = userProfile.stickLookSensitivity * CameraRigController.aimStickGlobalScale.value * 45f;
				Vector2 vector = new Vector2(player.GetAxisRaw(ModConfig.SnapTurn.Value ? 26 : 2), player.GetAxisRaw(3));
				Vector2 vector2 = new Vector2(player.GetAxisRaw(16), player.GetAxisRaw(17));
				if (ModConfig.LockedCameraPitch.Value)
                {
					vector2.y = 0;
                }

				if (!ModConfig.SnapTurn.Value)
				{
					ConditionalNegate(ref vector.x, userProfile.mouseLookInvertX);
					ConditionalNegate(ref vector.y, userProfile.mouseLookInvertY);
					ConditionalNegate(ref vector2.x, userProfile.stickLookInvertX);
					ConditionalNegate(ref vector2.y, userProfile.stickLookInvertY);
					float magnitude = vector2.magnitude;
					float num3 = magnitude;
					self.aimStickPostSmoothing = Vector2.zero;
					self.aimStickPostDualZone = Vector2.zero;
					self.aimStickPostExponent = Vector2.zero;
					if (CameraRigController.aimStickDualZoneSmoothing.value != 0f)
					{
						float maxDelta = Time.deltaTime / CameraRigController.aimStickDualZoneSmoothing.value;
						num3 = Mathf.Min(Mathf.MoveTowards(self.stickAimPreviousAcceleratedMagnitude, magnitude, maxDelta), magnitude);
						self.stickAimPreviousAcceleratedMagnitude = num3;
						self.aimStickPostSmoothing = ((magnitude != 0f) ? (vector2 * (num3 / magnitude)) : Vector2.zero);
					}
					float num4 = num3;
					float value = CameraRigController.aimStickDualZoneSlope.value;
					float num5;
					if (num4 <= CameraRigController.aimStickDualZoneThreshold.value)
					{
						num5 = 0f;
					}
					else
					{
						num5 = 1f - value;
					}
					num3 = value * num4 + num5;
					self.aimStickPostDualZone = ((magnitude != 0f) ? (vector2 * (num3 / magnitude)) : Vector2.zero);
					num3 = Mathf.Pow(num3, CameraRigController.aimStickExponent.value);
					self.aimStickPostExponent = ((magnitude != 0f) ? (vector2 * (num3 / magnitude)) : Vector2.zero);
					if (magnitude != 0f)
					{
						vector2 *= num3 / magnitude;
					}
					if (self.cameraMode == CameraRigController.CameraMode.PlayerBasic && self.targetBody && !self.targetBody.isSprinting)
					{
						AimAssistTarget exists = null;
						AimAssistTarget exists2 = null;
						float value2 = CameraRigController.aimStickAssistMinSize.value;
						float num6 = value2 * CameraRigController.aimStickAssistMaxSize.value;
						float value3 = CameraRigController.aimStickAssistMaxSlowdownScale.value;
						float value4 = CameraRigController.aimStickAssistMinSlowdownScale.value;
						float num7 = 0f;
						float value5 = 0f;
						float num8 = 0f;
						Vector2 v = Vector2.zero;
						Vector2 zero = Vector2.zero;
						Vector2 normalized = vector2.normalized;
						Vector3 vector3 = new Vector3(0.5f, 0.5f, 0f);
						for (int i = 0; i < AimAssistTarget.instancesList.Count; i++)
						{
							AimAssistTarget aimAssistTarget = AimAssistTarget.instancesList[i];
							if (aimAssistTarget.teamComponent.teamIndex != self.targetTeamIndex)
							{
								Vector3 vector4 = self.sceneCam.WorldToViewportPoint(aimAssistTarget.point0.position);
								Vector3 vector5 = self.sceneCam.WorldToViewportPoint(aimAssistTarget.point1.position);
								float num9 = Mathf.Lerp(vector4.z, vector5.z, 0.5f);
								if (num9 > 3f)
								{
									float num10 = 1f / num9;
									Vector2 vector6 = Util.ClosestPointOnLine(vector4, vector5, vector3) - vector3;
									float num11 = Mathf.Clamp01(Util.Remap(vector6.magnitude, value2 * aimAssistTarget.assistScale * num10, num6 * aimAssistTarget.assistScale * num10, 1f, 0f));
									float num12 = Mathf.Clamp01(Vector3.Dot(vector6, vector2.normalized));
									float num13 = num12 * num11;
									if (num7 < num11)
									{
										num7 = num11;
										exists2 = aimAssistTarget;
									}
									if (num13 > num8)
									{
										num7 = num11;
										value5 = num12;
										exists = aimAssistTarget;
										v = vector6;
									}
								}
							}
						}
						Vector2 vector7 = vector2;
						if (exists2)
						{
							float magnitude2 = vector2.magnitude;
							float d = Mathf.Clamp01(Util.Remap(1f - num7, 0f, 1f, value3, value4));
							vector7 *= d;
						}
						if (exists)
						{
							vector7 = Vector3.RotateTowards(vector7, v, Util.Remap(value5, 1f, 0f, CameraRigController.aimStickAssistMaxDelta.value, CameraRigController.aimStickAssistMinDelta.value), 0f);
						}
						vector2 = vector7;

						if (ModConfig.LockedCameraPitch.Value)
						{
							vector2.y = 0;
						}
					}
					num14 = vector.x * mouseLookSensitivity * userProfile.mouseLookScaleX + vector2.x * num2 * userProfile.stickLookScaleX * Time.deltaTime;
					num15 = vector.y * mouseLookSensitivity * userProfile.mouseLookScaleY + vector2.y * num2 * userProfile.stickLookScaleY * Time.deltaTime;
				}
				else
                {
					wasTurningLeft = isTurningLeft;
					wasTurningRight = isTurningRight;

					isTurningLeft = vector.x < -0.8f || vector2.x < -0.8f;
					isTurningRight = vector.x > 0.8f || vector2.x > 0.8f;

					num14 = 0f;

					if (justTurnedLeft)
						num14 = -ModConfig.SnapTurnAngle.Value;
					else if (justTurnedRight)
						num14 = ModConfig.SnapTurnAngle.Value;

					num15 = 0f;
                }
			}
			else
			{
				num14 = 0f;
				num15 = 0f;
			}
			NetworkUser networkUser = Util.LookUpBodyNetworkUser(self.target);
			NetworkedViewAngles networkedViewAngles = null;
			if (networkUser)
			{
				networkedViewAngles = networkUser.GetComponent<NetworkedViewAngles>();
			}
			self.targetTeamIndex = TeamIndex.None;
			bool flag2 = false;
			self.targetParams = null;
			if (self.target)
			{
				self.targetBody = self.target.GetComponent<CharacterBody>();
				if (self.targetBody)
				{
					flag2 = self.targetBody.isSprinting;
					if (self.targetBody.currentVehicle)
					{
						self.targetParams = self.targetBody.currentVehicle.GetComponent<CameraTargetParams>();
					}
				}
				if (!self.targetParams)
				{
					self.targetParams = self.target.GetComponent<CameraTargetParams>();
				}
				TeamComponent component = self.target.GetComponent<TeamComponent>();
				if (component)
				{
					self.targetTeamIndex = component.teamIndex;
				}
			}
			Vector3 vector8 = self.desiredCameraState.position;
			if (self.targetParams)
			{
				Vector3 position = self.target.transform.position;
				Vector3 cameraPivotPosition = self.targetParams.cameraPivotPosition;
				if (self.targetParams.dontRaycastToPivot)
				{
					vector8 = cameraPivotPosition;
				}
				else
				{
					Vector3 direction = cameraPivotPosition - position;
					float magnitude3 = direction.magnitude;
					Ray ray = new Ray(position, direction);
					float num16 = self.Raycast(ray, magnitude3, self.targetParams.cameraParams.wallCushion);
					Debug.DrawRay(ray.origin, ray.direction * magnitude3, Color.green, Time.deltaTime);
					Debug.DrawRay(ray.origin, ray.direction * num16, Color.red, Time.deltaTime);
					vector8 = ray.GetPoint(num16);
				}
			}
			if (self.cameraMode == CameraRigController.CameraMode.PlayerBasic || self.cameraMode == CameraRigController.CameraMode.SpectateUser)
			{
				float min = -89.9f;
				float max = 89.9f;
				Vector3 idealLocalCameraPos = new Vector3(0f, 0f, 0f);
				float num17 = 0.1f;
				if (self.targetParams)
				{
					min = self.targetParams.cameraParams.minPitch;
					max = self.targetParams.cameraParams.maxPitch;
					idealLocalCameraPos = self.targetParams.idealLocalCameraPos;
					num17 = self.targetParams.cameraParams.wallCushion;
					if (self.targetParams.currentAimMode == CameraTargetParams.AimType.FirstPerson)
					{
						self.firstPersonTarget = self.target;
					}
					if (self.targetParams.fovOverride >= 0f && !ModConfig.SnapTurn.Value)
					{
						num = self.targetParams.fovOverride;
						num14 *= num / self.baseFov;
						num15 *= num / self.baseFov;
					}
					if (self.targetBody && flag2 && CameraRigController.enableSprintSensitivitySlowdown.value && !ModConfig.SnapTurn.Value)
					{
						num14 *= 0.5f;
						num15 *= 0.5f;
					}
				}
				if (self.sprintingParticleSystem)
				{
					ParticleSystem.MainModule main = self.sprintingParticleSystem.main;
					if (flag2)
					{
						main.loop = true;
						if (!self.sprintingParticleSystem.isPlaying)
						{
							self.sprintingParticleSystem.Play();
						}
					}
					else
					{
						main.loop = false;
					}
				}
				if (self.cameraMode == CameraRigController.CameraMode.PlayerBasic)
				{
					float num18 = self.pitch - num15;
					if (ModConfig.LockedCameraPitch.Value || ModConfig.SnapTurn.Value)
						num18 = 0;

					float num19 = self.yaw + num14;
					self.pitch = Mathf.Clamp(num18, min, max);
					self.yaw = Mathf.Repeat(num19, 360f);
				}
				else if (self.cameraMode == CameraRigController.CameraMode.SpectateUser && self.target)
				{
					if (networkedViewAngles)
					{
						self.SetPitchYaw(networkedViewAngles.viewAngles);
					}
					else
					{
						InputBankTest component2 = self.target.GetComponent<InputBankTest>();
						if (component2)
						{
							self.SetPitchYawFromLookVector(component2.aimDirection);
						}
					}
				}
				self.desiredCameraState.rotation = Quaternion.Euler(self.pitch, self.yaw, 0f);
				Vector3 direction2 = vector8 + self.desiredCameraState.rotation * idealLocalCameraPos - vector8;
				float num20 = direction2.magnitude;
				float num21 = (1f + self.pitch / -90f) * 0.5f;
				num20 *= Mathf.Sqrt(1f - num21);
				if (num20 < 0.25f)
				{
					num20 = 0.25f;
				}
				Ray ray2 = new Ray(vector8, direction2);
				float num22 = self.Raycast(new Ray(vector8, direction2), num20, num17 - 0.01f);
				Debug.DrawRay(ray2.origin, ray2.direction * num20, Color.yellow, Time.deltaTime);
				Debug.DrawRay(ray2.origin, ray2.direction * num22, Color.red, Time.deltaTime);
				self.currentCameraDistance = Mathf.Min(num22, Mathf.SmoothDamp(self.currentCameraDistance, num22, ref self.cameraDistanceVelocity, 0.5f));
				self.desiredCameraState.position = vector8 + direction2.normalized * self.currentCameraDistance;
				if (networkedViewAngles && networkedViewAngles.hasEffectiveAuthority)
				{
					networkedViewAngles.viewAngles = new PitchYawPair(self.pitch, self.yaw);
				}
			}
			if (self.targetBody)
			{
				num *= (self.targetBody.isSprinting ? 1.3f : 1f);
			}
			self.desiredCameraState.fov = Mathf.SmoothDamp(self.desiredCameraState.fov, num, ref self.fovVelocity, 0.2f, float.PositiveInfinity, Time.deltaTime);
			if (self.hud)
			{
				CharacterMaster targetMaster = self.targetBody ? self.targetBody.master : null;
				self.hud.targetMaster = targetMaster;
			}
			self.UpdateCrosshair(vector8);
			CameraState cameraState = self.desiredCameraState;
			if (self.overrideCam != null)
			{
				if ((UnityEngine.Object)self.overrideCam)
				{
					self.overrideCam.GetCameraState(self, ref cameraState);
				}
				else
				{
					self.overrideCam = null;
				}
			}
			if (self.lerpCameraTime >= 1f)
			{
				self.currentCameraState = cameraState;
			}
			else
			{
				self.currentCameraState = CameraState.Lerp(ref self.lerpCameraState, ref cameraState, CameraRigController.RemapLerpTime(self.lerpCameraTime));
			}
			self.SetCameraState(self.currentCameraState);
		}

        private static void ConditionalNegate(ref float value, bool invert)
        {
			value = invert ? -value : value;
        }

        private static void SetBodyInvisible(On.RoR2.Run.orig_Update orig, Run self)
        {
            Renderer[] renderers = LocalUserManager.GetFirstLocalUser()?.cachedBody?.modelLocator?.modelTransform?.gameObject.GetComponentsInChildren<Renderer>(true);
            if (renderers != null)
            {
                foreach (Renderer renderer in renderers)
                {
                    if (renderer.gameObject.activeSelf)
                    {
                        renderer.gameObject.SetActive(false);
                    }
                }
            }

            orig(self);
        }

        private static void SetCameraStateOverride(On.RoR2.CameraRigController.orig_SetCameraState orig, CameraRigController self, CameraState cameraState)
        {
            if (Run.instance && self.cameraMode == CameraRigController.CameraMode.PlayerBasic)
            {
                if (ModConfig.FirstPerson.Value)
                {
                    if (!VRCameraWrapper.instance)
                    {
                        GameObject wrapperObject = new GameObject("VR Camera Wrapper");
						VRCameraWrapper.instance = wrapperObject.AddComponent<VRCameraWrapper>();
						VRCameraWrapper.instance.Init(self);
                    }
					VRCameraWrapper.instance.UpdateRotation(cameraState);

                    cameraState.rotation = self.sceneCam.transform.rotation;

                    Vector3 pos = VRCameraWrapper.instance.transform.position;

                    if (self.targetBody)
                    {
                        pos = self.targetBody.transform.position;

                        CapsuleCollider collider = self.targetBody.GetComponent<CapsuleCollider>();

                        if (collider)
                            pos.y += (collider.height) / 2;
                    }

					VRCameraWrapper.instance.transform.position = pos;
                }
            }

            orig(self, cameraState);
        }

        private static void RemoveFOVChange(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            c.GotoNext(
                x => x.MatchLdarg(0),
                x => x.MatchLdarg(1),
                x => x.MatchLdfld<CameraState>("fov")
                );

            ILLabel breakLabel = c.IncomingLabels.ToList().First<ILLabel>();

            while (c.Next.OpCode.Code != Code.Ret)
            {
                c.Remove();
            }

            ILLabel retLabel = c.MarkLabel();

            c.GotoPrev(
                x => x.MatchBr(breakLabel)
                );
            c.Remove();
            c.Emit(OpCodes.Br_S, retLabel);
        }

        private static Ray GetVRCrosshairRaycastRay(On.RoR2.CameraRigController.orig_GetCrosshairRaycastRay orig, RoR2.CameraRigController self, Vector2 crosshairOffset, Vector3 raycastStartPlanePoint)
        {
			if (MotionControls.HandsReady)
            {
				return MotionControls.GetHandRayByDominance(true);
            }

            if (!self.sceneCam)
            {
                return default(Ray);
            }
            float fieldOfView = self.sceneCam.fieldOfView;
            float num = fieldOfView * self.sceneCam.aspect;
            Quaternion quaternion = Quaternion.Euler(crosshairOffset.y * fieldOfView, crosshairOffset.x * num, 0f);
            quaternion = self.sceneCam.transform.rotation * quaternion;
            return new Ray(Vector3.ProjectOnPlane(self.sceneCam.transform.position - raycastStartPlanePoint, self.sceneCam.transform.rotation * Vector3.forward) + raycastStartPlanePoint, quaternion * Vector3.forward);
        }
    }
}
