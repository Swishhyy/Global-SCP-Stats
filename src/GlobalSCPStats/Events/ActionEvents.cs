using System;
using System.Collections.Generic;
using Exiled.Events.EventArgs.Interfaces;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.Features;
using GlobalSCPStats.Core;
using H = Exiled.Events.Handlers;

namespace GlobalSCPStats.Events;

// Only action names and counts are retained; event arguments are never serialized.
public sealed class ActionEvents
{
    private readonly StatsRuntime runtime;
    private readonly List<Action> unsubscribe = new List<Action>();
    public ActionEvents(StatsRuntime runtime) => this.runtime = runtime;

    public void Register()
    {
        Bind(H.Player.UsedItem, "player.item_used");
        Bind(H.Player.ItemAdded, "player.item_added");
        Bind(H.Player.ItemRemoved, "player.item_removed");
        Bind(H.Player.DroppedItem, "player.item_dropped");
        Bind(H.Player.DroppedAmmo, "player.ammo_dropped");
        Bind(H.Player.ThrownProjectile, "player.projectile_thrown");
        Bind(H.Player.Shot, "player.shot");
        Bind(H.Player.ReloadedWeapon, "player.weapon_reloaded");
        Bind(H.Player.UnloadedWeapon, "player.weapon_unloaded");
        Bind(H.Player.ChangedItem, "player.item_changed");
        Bind(H.Player.RemovedHandcuffs, "player.handcuffs_removed");
        Bind(H.Player.Interacted, "player.interacted");
        Bind(H.Player.RoomChanged, "player.room_changed");
        Bind(H.Player.ZoneChanged, "player.zone_changed");
        Bind(H.Player.ChangedEmotion, "player.emotion_changed");

        // EXILED exposes these as cancellable hooks. Their counters describe allowed attempts.
        Bind(H.Player.InteractingDoor, "player.door_interacting");
        Bind(H.Player.InteractingElevator, "player.elevator_interacting");
        Bind(H.Player.InteractingLocker, "player.locker_interacting");
        Bind(H.Player.UnlockingGenerator, "player.generator_unlocking");
        Bind(H.Player.OpeningGenerator, "player.generator_opening");
        Bind(H.Player.ClosingGenerator, "player.generator_closing");
        Bind(H.Player.ActivatingGenerator, "player.generator_activating");
        Bind(H.Player.StoppingGenerator, "player.generator_stopping");
        Bind(H.Player.ActivatingWarheadPanel, "player.warhead_panel_activating");
        Bind(H.Player.ActivatingWorkstation, "player.workstation_activating");
        Bind(H.Player.DeactivatingWorkstation, "player.workstation_deactivating");
        Bind(H.Player.Handcuffing, "player.handcuffing");
        Bind(H.Player.EscapingPocketDimension, "player.pocket_dimension_escaping");
        Bind(H.Player.EnteringPocketDimension, "player.pocket_dimension_entering");
        Bind(H.Player.FlippingCoin, "player.coin_flipping");
        Bind(H.Player.TogglingFlashlight, "player.flashlight_toggling");
        Bind(H.Player.TogglingWeaponFlashlight, "player.weapon_flashlight_toggling");
        Bind(H.Player.TogglingRadio, "player.radio_toggling");
        Bind(H.Player.ChangingRadioPreset, "player.radio_preset_changing");
        Bind(H.Player.Jumping, "player.jumping");
        Bind(H.Player.Landing, "player.landing");
        Bind(H.Player.DamagingWindow, "player.window_damaging");
        Bind(H.Player.DamagingDoor, "player.door_damaging");
        Bind(H.Player.IntercomSpeaking, "player.intercom_speaking");
        Bind(H.Player.AimingDownSight, "player.aiming_down_sight");
        Bind(H.Player.DryfiringWeapon, "player.weapon_dryfiring");
        Bind(H.Player.RotatingRevolver, "player.revolver_rotating");
        Bind(H.Player.ChangingDisruptorMode, "player.disruptor_mode_changing");
        Bind(H.Player.ChangingMicroHIDState, "player.microhid_state_changing");
        Bind(H.Player.UsingMicroHIDEnergy, "player.microhid_energy_using");
        Bind(H.Player.MicroHIDOpeningDoor, "player.microhid_door_opening");
        Bind(H.Player.ExplodingMicroHID, "player.microhid_exploding");
        Bind(H.Player.ChangingMoveState, "player.move_state_changing");
        Bind(H.Player.PlayingAudioLog, "player.audio_log_playing");
        Bind(H.Player.InteractingEmergencyButton, "player.emergency_button_interacting");
        Bind(H.Player.ChangingWearables, "player.wearables_changing");
        Bind(H.Player.Scp1576TransmissionEnded, "scp1576.transmission_ended");
        Bind(H.Warhead.Stopping, "warhead.stopping");
        Bind(H.Warhead.ChangingLeverStatus, "warhead.lever_changing");
        Bind(H.Scp049.FinishingRecall, "scp049.recall_finishing");
        Bind(H.Scp049.ActivatingSense, "scp049.sense_activating");
        Bind(H.Scp049.SendingCall, "scp049.call_sending");
        Bind(H.Scp0492.ConsumedCorpse, "scp0492.corpse_consumed");
        Bind(H.Scp0492.TriggeringBloodlust, "scp0492.bloodlust_triggering");
        Bind(H.Scp079.GainingLevel, "scp079.level_gaining");
        Bind(H.Scp079.ChangingCamera, "scp079.camera_changing");
        Bind(H.Scp079.InteractingTesla, "scp079.tesla_interacting");
        Bind(H.Scp079.TriggeringDoor, "scp079.door_triggering");
        Bind(H.Scp079.LockingDown, "scp079.locking_down");
        Bind(H.Scp079.RoomBlackout, "scp079.room_blackout");
        Bind(H.Scp079.ZoneBlackout, "scp079.zone_blackout");
        Bind(H.Scp079.Pinging, "scp079.pinging");
        Bind(H.Scp079.GainingExperience, "scp079.experience_gaining");
        Bind(H.Scp079.ElevatorTeleporting, "scp079.elevator_teleporting");
        Bind(H.Scp079.ChangingSpeakerStatus, "scp079.speaker_changing");
        Bind(H.Scp096.Enraging, "scp096.enraging");
        Bind(H.Scp096.Charging, "scp096.charging");
        Bind(H.Scp096.CalmingDown, "scp096.calming_down");
        Bind(H.Scp096.StartPryingGate, "scp096.gate_prying");
        Bind(H.Scp096.TryingNotToCry, "scp096.cry_suppressing");
        Bind(H.Scp106.Teleporting, "scp106.teleporting");
        Bind(H.Scp106.Stalking, "scp106.stalking");
        Bind(H.Scp106.ExitStalking, "scp106.stalking_exiting");
        Bind(H.Scp127.GainedExperience, "scp127.experience_gained");
        Bind(H.Scp1344.ChangedStatus, "scp1344.status_changed");
        Bind(H.Scp1344.Deactivated, "scp1344.deactivated");
        Bind(H.Scp1509.Resurrecting, "scp1509.resurrecting");
        Bind(H.Scp1509.TriggeringAttack, "scp1509.attack_triggering");
        Bind(H.Scp173.Blinking, "scp173.blinking");
        Bind(H.Scp173.PlacingTantrum, "scp173.tantrum_placing");
        Bind(H.Scp173.UsingBreakneckSpeeds, "scp173.breakneck_using");
        Bind(H.Scp244.UsingScp244, "scp244.using");
        Bind(H.Scp3114.Disguised, "scp3114.disguised");
        Bind(H.Scp3114.Revealed, "scp3114.revealed");
        Bind(H.Scp3114.Slapped, "scp3114.slapped");
        Bind(H.Scp3114.Dancing, "scp3114.dancing");
        Bind(H.Scp3114.Strangling, "scp3114.strangling");
        Bind(H.Scp330.EatenScp330, "scp330.candy_eaten");
        Bind(H.Scp330.InteractingScp330, "scp330.interacting");
        Bind(H.Scp914.Activating, "scp914.activating");
        Bind(H.Scp914.ChangingKnobSetting, "scp914.knob_changing");
        Bind(H.Scp914.UpgradedInventoryItem, "scp914.inventory_item_upgraded");
        Bind(H.Scp939.Lunging, "scp939.lunging");
        Bind(H.Scp939.PlacedAmnesticCloud, "scp939.cloud_placed");
        Bind(H.Scp939.ChangingFocus, "scp939.focus_changing");
        Bind(H.Scp939.PlayingVoice, "scp939.voice_playing");
        Bind(H.Scp939.PlayingFootstep, "scp939.footstep_playing");
        Bind(H.Scp939.PlayingSound, "scp939.sound_playing");
        Bind(H.Scp939.Clawed, "scp939.clawed");
        Bind(H.Scp939.PlacingMimicPoint, "scp939.mimic_point_placing");
        H.Player.Hurt += OnHurt;
        H.Player.Healed += OnHealed;
    }

    public void Unregister()
    {
        foreach (Action remove in unsubscribe) remove();
        unsubscribe.Clear();
        H.Player.Hurt -= OnHurt;
        H.Player.Healed -= OnHealed;
    }

    private void Bind<T>(Event<T> source, string action) where T : IPlayerEvent
    {
        CustomEventHandler<T> handler = ev =>
        {
            if (ev is IDeniableEvent deniable && !deniable.IsAllowed) return;
            if (!StatsEvents.IsEligible(ev.Player)) return;
            StatsEvents.SynchronizePrivacy(runtime, ev.Player);
            runtime.Action(ev.Player.UserId, action);
        };
        source.Subscribe(handler);
        unsubscribe.Add(() => source.Unsubscribe(handler));
    }

    private void OnHurt(HurtEventArgs ev)
    {
        if (!StatsEvents.IsEligible(ev.Player)) return;
        StatsEvents.SynchronizePrivacy(runtime, ev.Player);
        StatsEvents.SynchronizePrivacy(runtime, ev.Attacker);
        runtime.Damage(ev.Player.UserId, StatsEvents.IsEligible(ev.Attacker) ? ev.Attacker.UserId : null, ev.Amount);
    }

    private void OnHealed(HealedEventArgs ev)
    {
        if (!StatsEvents.IsEligible(ev.Player)) return;
        StatsEvents.SynchronizePrivacy(runtime, ev.Player);
        runtime.Heal(ev.Player.UserId, ev.HealedAmount);
    }
}
