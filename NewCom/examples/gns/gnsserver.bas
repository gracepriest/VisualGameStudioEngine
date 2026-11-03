' gnsserver.bas — BasicLang binding for NewCOM GNSServer component

Declare Function GNSServer_Create Lib "gnsserver_newcom" () As Integer
Declare Function GNSServer_Start Lib "gnsserver_newcom" (h As Integer, addr As String) As Integer
Declare Function GNSServer_Accept Lib "gnsserver_newcom" (h As Integer, outConn As ByRef Sub) As Integer
Declare Sub GNSServer_Stop Lib "gnsserver_newcom" (h As Integer)
Declare Function GNSServer_GetClientCount Lib "gnsserver_newcom" (h As Integer) As Integer
Declare Function GNSServer_AddRef Lib "gnsserver_newcom" (h As Integer) As Integer
Declare Sub GNSServer_Release Lib "gnsserver_newcom" (h As Integer)

Class GNSServer
    Private _handle As Integer

    Public Sub New()
        _handle = GNSServer_Create()
    End Sub

    Public Function Start(addr As String) As Integer
        Return GNSServer_Start(_handle, addr)
    End Function

    Public Function Accept(outConn As ByRef Sub) As Integer
        Return GNSServer_Accept(_handle, outConn)
    End Function

    Public Sub Stop()
        GNSServer_Stop(_handle)
    End Sub

    Public Function GetClientCount() As Integer
        Return GNSServer_GetClientCount(_handle)
    End Function

    Public Sub Close()
        GNSServer_Release(_handle)
        _handle = 0
    End Sub
End Class
