# Tracked actions

The connector currently binds 105 gameplay hooks below, plus the combat, healing, and participation counters listed separately. This is the supported catalog for this version; it does not claim to observe every internal game event.

Every tracked action is eligible for XP. The default reward is 1 XP, with a 2-second per-action cooldown and a rolling 1,000 XP per-player/minute cap. Rewards/cooldowns are configurable; setting a reward to zero or disabling XP preserves all counters. Default overrides are shown in the example config.

Actions are counted only during active rounds for verified supported platform accounts. Warmup, host/NPC actions, administrative commands, and raw network/voice/chat content are excluded. A single physical activity may produce different counters, such as an item being used and removed; do not sum action categories as if they were unique physical events.

EXILED provides both completed-event hooks and cancellable hooks. Keys ending in interacting, changing, activating, using, and similar forms generally describe attempts. A cancellable hook is counted only when IsAllowed is true when this listener runs; a later listener can still cancel it. They must not be interpreted as confirmed objective completions. New completed hooks should replace attempt-based metrics when framework support becomes available.

## Built-in counters

| Action | Trigger | Default XP |
| --- | --- | --- |
| round_played | First active participation in a round, including a late spawn | 20 |
| kill | Eligible player death attributed to another tracked player; includes friendly kills | 50 |
| death | Eligible player death, including world damage and suicide | 1 |
| escape | First confirmed escape for a player in a round | 100 |
| damage_dealt | Positive reported damage to another eligible player | 2 |
| damage_received | Positive reported damage, including world/self damage | 1 |
| healed | Positive completed healing reported by EXILED | 5 |

Damage and healing amounts are accumulated separately from occurrence counters. Reported damage is the amount exposed by the EXILED post-damage event, not a separate reconstruction of health/shield/armor changes.

## Gameplay hooks

| Action key | EXILED hook |
| --- | --- |
| player.aiming_down_sight | Player.AimingDownSight |
| player.ammo_dropped | Player.DroppedAmmo |
| player.audio_log_playing | Player.PlayingAudioLog |
| player.coin_flipping | Player.FlippingCoin |
| player.disruptor_mode_changing | Player.ChangingDisruptorMode |
| player.door_damaging | Player.DamagingDoor |
| player.door_interacting | Player.InteractingDoor |
| player.elevator_interacting | Player.InteractingElevator |
| player.emergency_button_interacting | Player.InteractingEmergencyButton |
| player.emotion_changed | Player.ChangedEmotion |
| player.flashlight_toggling | Player.TogglingFlashlight |
| player.generator_activating | Player.ActivatingGenerator |
| player.generator_closing | Player.ClosingGenerator |
| player.generator_opening | Player.OpeningGenerator |
| player.generator_stopping | Player.StoppingGenerator |
| player.generator_unlocking | Player.UnlockingGenerator |
| player.handcuffing | Player.Handcuffing |
| player.handcuffs_removed | Player.RemovedHandcuffs |
| player.interacted | Player.Interacted |
| player.intercom_speaking | Player.IntercomSpeaking |
| player.item_added | Player.ItemAdded |
| player.item_changed | Player.ChangedItem |
| player.item_dropped | Player.DroppedItem |
| player.item_removed | Player.ItemRemoved |
| player.item_used | Player.UsedItem |
| player.jumping | Player.Jumping |
| player.landing | Player.Landing |
| player.locker_interacting | Player.InteractingLocker |
| player.microhid_door_opening | Player.MicroHIDOpeningDoor |
| player.microhid_energy_using | Player.UsingMicroHIDEnergy |
| player.microhid_exploding | Player.ExplodingMicroHID |
| player.microhid_state_changing | Player.ChangingMicroHIDState |
| player.move_state_changing | Player.ChangingMoveState |
| player.pocket_dimension_entering | Player.EnteringPocketDimension |
| player.pocket_dimension_escaping | Player.EscapingPocketDimension |
| player.projectile_thrown | Player.ThrownProjectile |
| player.radio_preset_changing | Player.ChangingRadioPreset |
| player.radio_toggling | Player.TogglingRadio |
| player.revolver_rotating | Player.RotatingRevolver |
| player.room_changed | Player.RoomChanged |
| player.shot | Player.Shot |
| player.warhead_panel_activating | Player.ActivatingWarheadPanel |
| player.weapon_dryfiring | Player.DryfiringWeapon |
| player.weapon_flashlight_toggling | Player.TogglingWeaponFlashlight |
| player.weapon_reloaded | Player.ReloadedWeapon |
| player.weapon_unloaded | Player.UnloadedWeapon |
| player.wearables_changing | Player.ChangingWearables |
| player.window_damaging | Player.DamagingWindow |
| player.workstation_activating | Player.ActivatingWorkstation |
| player.workstation_deactivating | Player.DeactivatingWorkstation |
| player.zone_changed | Player.ZoneChanged |
| scp049.call_sending | Scp049.SendingCall |
| scp049.recall_finishing | Scp049.FinishingRecall |
| scp049.sense_activating | Scp049.ActivatingSense |
| scp0492.bloodlust_triggering | Scp0492.TriggeringBloodlust |
| scp0492.corpse_consumed | Scp0492.ConsumedCorpse |
| scp079.camera_changing | Scp079.ChangingCamera |
| scp079.door_triggering | Scp079.TriggeringDoor |
| scp079.elevator_teleporting | Scp079.ElevatorTeleporting |
| scp079.experience_gaining | Scp079.GainingExperience |
| scp079.level_gaining | Scp079.GainingLevel |
| scp079.locking_down | Scp079.LockingDown |
| scp079.pinging | Scp079.Pinging |
| scp079.room_blackout | Scp079.RoomBlackout |
| scp079.speaker_changing | Scp079.ChangingSpeakerStatus |
| scp079.tesla_interacting | Scp079.InteractingTesla |
| scp079.zone_blackout | Scp079.ZoneBlackout |
| scp096.calming_down | Scp096.CalmingDown |
| scp096.charging | Scp096.Charging |
| scp096.cry_suppressing | Scp096.TryingNotToCry |
| scp096.enraging | Scp096.Enraging |
| scp096.gate_prying | Scp096.StartPryingGate |
| scp106.stalking | Scp106.Stalking |
| scp106.stalking_exiting | Scp106.ExitStalking |
| scp106.teleporting | Scp106.Teleporting |
| scp127.experience_gained | Scp127.GainedExperience |
| scp1344.deactivated | Scp1344.Deactivated |
| scp1344.status_changed | Scp1344.ChangedStatus |
| scp1509.attack_triggering | Scp1509.TriggeringAttack |
| scp1509.resurrecting | Scp1509.Resurrecting |
| scp1576.transmission_ended | Player.Scp1576TransmissionEnded |
| scp173.blinking | Scp173.Blinking |
| scp173.breakneck_using | Scp173.UsingBreakneckSpeeds |
| scp173.tantrum_placing | Scp173.PlacingTantrum |
| scp244.using | Scp244.UsingScp244 |
| scp3114.dancing | Scp3114.Dancing |
| scp3114.disguised | Scp3114.Disguised |
| scp3114.revealed | Scp3114.Revealed |
| scp3114.slapped | Scp3114.Slapped |
| scp3114.strangling | Scp3114.Strangling |
| scp330.candy_eaten | Scp330.EatenScp330 |
| scp330.interacting | Scp330.InteractingScp330 |
| scp914.activating | Scp914.Activating |
| scp914.inventory_item_upgraded | Scp914.UpgradedInventoryItem |
| scp914.knob_changing | Scp914.ChangingKnobSetting |
| scp939.clawed | Scp939.Clawed |
| scp939.cloud_placed | Scp939.PlacedAmnesticCloud |
| scp939.focus_changing | Scp939.ChangingFocus |
| scp939.footstep_playing | Scp939.PlayingFootstep |
| scp939.lunging | Scp939.Lunging |
| scp939.mimic_point_placing | Scp939.PlacingMimicPoint |
| scp939.sound_playing | Scp939.PlayingSound |
| scp939.voice_playing | Scp939.PlayingVoice |
| warhead.lever_changing | Warhead.ChangingLeverStatus |
| warhead.stopping | Warhead.Stopping |

To add a hook, add a typed Bind call in ActionEvents.Register, choose a stable action key, and document whether it describes a completion or an attempt. Only the name/count is recorded. Privacy and administrative events should not be added indiscriminately.
