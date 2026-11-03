' gnsclient.bas — BasicLang binding for NewCOM GNSClient component

Declare Function GNSClient_Create Lib "gnsclient_newcom" () As Integer
Declare Function GNSClient_Connect Lib "gnsclient_newcom" (h As Integer, addr As String) As Integer
Declare Function GNSClient_Send Lib "gnsclient_newcom" (h As Integer, data As Integer, size As Integer) As Integer
Declare Function GNSClient_Receive Lib "gnsclient_newcom" (h As Integer, buf As Integer, outSize As ByRef Integer) As Integer
Declare Sub GNSClient_Disconnect Lib "gnsclient_newcom" (h As Integer)
Declare Function GNSClient_GetState Lib "gnsclient_newcom" (h As Integer) As Integer
Declare Function GNSClient_AddRef Lib "gnsclient_newcom" (h As Integer) As Integer
Declare Sub GNSClient_Release Lib "gnsclient_newcom" (h As Integer)

Class GNSClient
    Private _handle As Integer

    Public Sub New()
        _handle = GNSClient_Create()
    End Sub

    Public Function Connect(addr As String) As Integer
        Return GNSClient_Connect(_handle, addr)
    End Function

    Public Function Send(data As Integer, size As Integer) As Integer
        Return GNSClient_Send(_handle, data, size)
    End Function

    Public Function Receive(buf As Integer, outSize As ByRef Integer) As Integer
        Return GNSClient_Receive(_handle, buf, outSize)
    End Function

    Public Sub Disconnect()
        GNSClient_Disconnect(_handle)
    End Sub

    Public Function GetState() As Integer
        Return GNSClient_GetState(_handle)
    End Function

    Public Sub Close()
        GNSClient_Release(_handle)
        _handle = 0
    End Sub
End Class
