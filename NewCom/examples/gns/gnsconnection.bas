' gnsconnection.bas — BasicLang binding for NewCOM GNSConnection component

Declare Function GNSConnection_Create Lib "gnsconnection_newcom" () As Integer
Declare Function GNSConnection_Send Lib "gnsconnection_newcom" (h As Integer, data As Integer, size As Integer) As Integer
Declare Function GNSConnection_Receive Lib "gnsconnection_newcom" (h As Integer, buf As Integer, outSize As ByRef Integer) As Integer
Declare Sub GNSConnection_Close Lib "gnsconnection_newcom" (h As Integer)
Declare Function GNSConnection_GetState Lib "gnsconnection_newcom" (h As Integer) As Integer
Declare Function GNSConnection_GetRemoteAddr Lib "gnsconnection_newcom" (h As Integer, outAddr As ByRef Integer) As Integer
Declare Function GNSConnection_AddRef Lib "gnsconnection_newcom" (h As Integer) As Integer
Declare Sub GNSConnection_Release Lib "gnsconnection_newcom" (h As Integer)

Class GNSConnection
    Private _handle As Integer

    Public Sub New()
        _handle = GNSConnection_Create()
    End Sub

    Public Function Send(data As Integer, size As Integer) As Integer
        Return GNSConnection_Send(_handle, data, size)
    End Function

    Public Function Receive(buf As Integer, outSize As ByRef Integer) As Integer
        Return GNSConnection_Receive(_handle, buf, outSize)
    End Function

    Public Sub Close()
        GNSConnection_Close(_handle)
    End Sub

    Public Function GetState() As Integer
        Return GNSConnection_GetState(_handle)
    End Function

    Public Function GetRemoteAddr(outAddr As ByRef Integer) As Integer
        Return GNSConnection_GetRemoteAddr(_handle, outAddr)
    End Function

    Public Sub Close()
        GNSConnection_Release(_handle)
        _handle = 0
    End Sub
End Class
