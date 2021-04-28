// Code generated by "stringer -type=AuthClientMessage"; DO NOT EDIT.

package message

import "strconv"

func _() {
	// An "invalid array index" compiler error signifies that the constant values have changed.
	// Re-run the stringer command to generate them again.
	var x [1]struct{}
	_ = x[AuthClientLoginPacket-0]
	_ = x[AuthClientAboutToPlayPacket-2]
	_ = x[AuthClientLogoutPacket-3]
	_ = x[AuthClientServerListExtPacket-5]
	_ = x[AuthClientSCCheckPacket-6]
	_ = x[AuthClientConnectToQueuePacket-7]
}

const (
	_AuthClientMessage_name_0 = "AuthClientLoginPacket"
	_AuthClientMessage_name_1 = "AuthClientAboutToPlayPacketAuthClientLogoutPacket"
	_AuthClientMessage_name_2 = "AuthClientServerListExtPacketAuthClientSCCheckPacketAuthClientConnectToQueuePacket"
)

var (
	_AuthClientMessage_index_1 = [...]uint8{0, 27, 49}
	_AuthClientMessage_index_2 = [...]uint8{0, 29, 52, 82}
)

func (i AuthClientMessage) String() string {
	switch {
	case i == 0:
		return _AuthClientMessage_name_0
	case 2 <= i && i <= 3:
		i -= 2
		return _AuthClientMessage_name_1[_AuthClientMessage_index_1[i]:_AuthClientMessage_index_1[i+1]]
	case 5 <= i && i <= 7:
		i -= 5
		return _AuthClientMessage_name_2[_AuthClientMessage_index_2[i]:_AuthClientMessage_index_2[i+1]]
	default:
		return "AuthClientMessage(" + strconv.FormatInt(int64(i), 10) + ")"
	}
}
