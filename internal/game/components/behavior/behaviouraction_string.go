// Code generated by "stringer -type=BehaviourAction"; DO NOT EDIT.

package behavior

import "strconv"

func _() {
	// An "invalid array index" compiler error signifies that the constant values have changed.
	// Re-run the stringer command to generate them again.
	var x [1]struct{}
	_ = x[BehaviourActionMoveTo-1]
	_ = x[BehaviourActionSpawn-4]
	_ = x[BehaviourActionActivate-6]
	_ = x[BehaviourActionKnockBack-10]
	_ = x[BehaviourActionKnockDown-11]
	_ = x[BehaviourActionStun-12]
	_ = x[BehaviourActionSearchForAttack-13]
	_ = x[BehaviourActionSpawnAnimation-14]
	_ = x[BehaviourActionUnSpawn-15]
	_ = x[BehaviourActionDodge-16]
	_ = x[BehaviourActionWarpTo-17]
	_ = x[BehaviourActionAmbush-18]
	_ = x[BehaviourActionMoveInDirectionAction-19]
	_ = x[BehaviourActionTurnAction-20]
	_ = x[BehaviourActionWander-21]
	_ = x[BehaviourActionFollow-22]
	_ = x[BehaviourActionPlayAnimation-32]
	_ = x[BehaviourActionFaceTarget-33]
	_ = x[BehaviourActionWait-34]
	_ = x[BehaviourActionImmobilize-35]
	_ = x[BehaviourActionIdle-47]
	_ = x[BehaviourActionRessurect-48]
	_ = x[BehaviourActionRemove-64]
	_ = x[BehaviourActionHide-65]
	_ = x[BehaviourActionSetBlocking-69]
	_ = x[BehaviourActionFlee-79]
	_ = x[BehaviourActionUseTarget-80]
	_ = x[BehaviourActionUsePosition-81]
	_ = x[BehaviourActionUse-82]
	_ = x[BehaviourActionUseItemTarget-83]
	_ = x[BehaviourActionUseItemPosition-84]
	_ = x[BehaviourActionUseItem-85]
	_ = x[BehaviourActionDoEffect-131]
	_ = x[BehaviourActionRetrieveItem-160]
	_ = x[BehaviourActionConvertItemsToGold-161]
	_ = x[BehaviourActionAttackTarget2-240]
	_ = x[BehaviourActionKill-254]
	_ = x[BehaviourActionDie-255]
}

const _BehaviourAction_name = "BehaviourActionMoveToBehaviourActionSpawnBehaviourActionActivateBehaviourActionKnockBackBehaviourActionKnockDownBehaviourActionStunBehaviourActionSearchForAttackBehaviourActionSpawnAnimationBehaviourActionUnSpawnBehaviourActionDodgeBehaviourActionWarpToBehaviourActionAmbushBehaviourActionMoveInDirectionActionBehaviourActionTurnActionBehaviourActionWanderBehaviourActionFollowBehaviourActionPlayAnimationBehaviourActionFaceTargetBehaviourActionWaitBehaviourActionImmobilizeBehaviourActionIdleBehaviourActionRessurectBehaviourActionRemoveBehaviourActionHideBehaviourActionSetBlockingBehaviourActionFleeBehaviourActionUseTargetBehaviourActionUsePositionBehaviourActionUseBehaviourActionUseItemTargetBehaviourActionUseItemPositionBehaviourActionUseItemBehaviourActionDoEffectBehaviourActionRetrieveItemBehaviourActionConvertItemsToGoldBehaviourActionAttackTarget2BehaviourActionKillBehaviourActionDie"

var _BehaviourAction_map = map[BehaviourAction]string{
	1:   _BehaviourAction_name[0:21],
	4:   _BehaviourAction_name[21:41],
	6:   _BehaviourAction_name[41:64],
	10:  _BehaviourAction_name[64:88],
	11:  _BehaviourAction_name[88:112],
	12:  _BehaviourAction_name[112:131],
	13:  _BehaviourAction_name[131:161],
	14:  _BehaviourAction_name[161:190],
	15:  _BehaviourAction_name[190:212],
	16:  _BehaviourAction_name[212:232],
	17:  _BehaviourAction_name[232:253],
	18:  _BehaviourAction_name[253:274],
	19:  _BehaviourAction_name[274:310],
	20:  _BehaviourAction_name[310:335],
	21:  _BehaviourAction_name[335:356],
	22:  _BehaviourAction_name[356:377],
	32:  _BehaviourAction_name[377:405],
	33:  _BehaviourAction_name[405:430],
	34:  _BehaviourAction_name[430:449],
	35:  _BehaviourAction_name[449:474],
	47:  _BehaviourAction_name[474:493],
	48:  _BehaviourAction_name[493:517],
	64:  _BehaviourAction_name[517:538],
	65:  _BehaviourAction_name[538:557],
	69:  _BehaviourAction_name[557:583],
	79:  _BehaviourAction_name[583:602],
	80:  _BehaviourAction_name[602:626],
	81:  _BehaviourAction_name[626:652],
	82:  _BehaviourAction_name[652:670],
	83:  _BehaviourAction_name[670:698],
	84:  _BehaviourAction_name[698:728],
	85:  _BehaviourAction_name[728:750],
	131: _BehaviourAction_name[750:773],
	160: _BehaviourAction_name[773:800],
	161: _BehaviourAction_name[800:833],
	240: _BehaviourAction_name[833:861],
	254: _BehaviourAction_name[861:880],
	255: _BehaviourAction_name[880:898],
}

func (i BehaviourAction) String() string {
	if str, ok := _BehaviourAction_map[i]; ok {
		return str
	}
	return "BehaviourAction(" + strconv.FormatInt(int64(i), 10) + ")"
}