package message

import (
	"RainbowRunner/internal/byter"
	"RainbowRunner/internal/connection"
	"RainbowRunner/internal/crypt"
	"fmt"
)

func HandleLoginMessage(conn *connection.Connection, reader *byter.Byter) error {
	decryptedLogin := crypt.DecryptDES(reader.Bytes(0x18), 0x18)
	remainingPassword := reader.Bytes(0x1E - 0x18)
	decryptedLogin = append(decryptedLogin, remainingPassword...)
	username := string(decryptedLogin[0:14])
	password := string(decryptedLogin[14:])
	fmt.Printf("Login attempt with %s:%s\n", username, password)

	/**
	00000000 linACLoginOkPacket struc ; (sizeof=0x38, align=0x4, copyof_811)
	00000000 baseclass_0 msgMessage ?
	00000010 m_sessionId1 dd ?
	00000014 m_sessionId2 dd ?
	00000018 m_updateKey1 dd ?
	0000001C m_updateKey2 dd ?
	00000020 m_payStat dd ?
	00000024 m_remainingTime dd ?
	00000028 m_quotaTime dd ?
	0000002C m_warnFlag dd ?
	00000030 m_loginFlag dd ?
	00000034 m_queueLevel db ?
	00000035 db ? ; undefined
	00000036 db ? ; undefined
	00000037 db ? ; undefined
	00000038 linACLoginOkPacket ends
	 */
	
	var response = byter.NewByter(make([]byte, 0, 128))

	response.WriteByte(0x03)
	response.WriteUInt32(0xFFEEFFEE)
	response.WriteUInt32(0xAABBAABB)
	response.WriteUInt32(0xDDCCDDCC)
	response.WriteUInt32(0xBBCCBBCC)
	response.WriteUInt32(0x00000000)
	response.WriteUInt32(0xFFFFFFFF)
	response.WriteUInt32(0xFFFFFFFF)
	response.WriteUInt32(0x00000000)
	response.WriteUInt32(0x00000000)
	response.WriteUInt32(0x00000000)
	response.WriteBool(true)
	response.WriteBool(true)
	response.WriteBool(true)

	err := conn.WriteMessageByter(response)

	if err != nil {
		return err
	}

	return nil
}
