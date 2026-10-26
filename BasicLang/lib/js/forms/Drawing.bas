' Portable control library - System.Drawing value and drawing types (task 28).
'
' Task 29 adds only the members the common Control properties convert. Slice 3 (task 40)
' owns the exhaustive WinForms value surface.
Namespace System.Drawing
    Public Class Color
        Public Property A As Integer
        Public Property R As Integer
        Public Property G As Integer
        Public Property B As Integer
        Public Property Name As String

        Public Sub New()
            Me.A = 0
            Me.Name = ""
        End Sub

        Public Shared Function VgsNamed(name As String) As Color
            Dim value As New Color()
            value.Name = name
            value.A = 255
            If name = "transparent" Then value.A = 0
            Return value
        End Function

        Public Shared Function FromName(name As String) As Color
            If name Is Nothing Then name = ""
            Return VgsNamed(name.ToLowerInvariant())
        End Function

        Public Shared Function FromArgb(r As Integer, g As Integer, b As Integer, Optional a As Integer = 255) As Color
            Dim value As New Color()
            value.R = r
            value.G = g
            value.B = b
            value.A = a
            value.Name = ""
            Return value
        End Function

        Public Function Equals(other As Color) As Boolean
            If other Is Nothing Then Return False
            Return Me.A = other.A AndAlso Me.R = other.R AndAlso Me.G = other.G AndAlso Me.B = other.B AndAlso Me.Name = other.Name
        End Function
        Public Shared ReadOnly Property Empty As Color
            Get
                Return New Color()
            End Get
        End Property

        Public Shared ReadOnly Property Transparent As Color
            Get
                Return VgsNamed("transparent")
            End Get
        End Property

        Public Shared ReadOnly Property Black As Color
            Get
                Return VgsNamed("black")
            End Get
        End Property

        Public Shared ReadOnly Property White As Color
            Get
                Return VgsNamed("white")
            End Get
        End Property

        Public Shared ReadOnly Property Gray As Color
            Get
                Return VgsNamed("gray")
            End Get
        End Property

        Public Shared ReadOnly Property Red As Color
            Get
                Return VgsNamed("red")
            End Get
        End Property

        Public Shared ReadOnly Property Green As Color
            Get
                Return VgsNamed("green")
            End Get
        End Property

        Public Shared ReadOnly Property Blue As Color
            Get
                Return VgsNamed("blue")
            End Get
        End Property

        Public Shared ReadOnly Property AliceBlue As Color
            Get
                Return VgsNamed("aliceblue")
            End Get
        End Property
        Public Shared ReadOnly Property AntiqueWhite As Color
            Get
                Return VgsNamed("antiquewhite")
            End Get
        End Property
        Public Shared ReadOnly Property Aqua As Color
            Get
                Return VgsNamed("aqua")
            End Get
        End Property
        Public Shared ReadOnly Property Aquamarine As Color
            Get
                Return VgsNamed("aquamarine")
            End Get
        End Property
        Public Shared ReadOnly Property Azure As Color
            Get
                Return VgsNamed("azure")
            End Get
        End Property
        Public Shared ReadOnly Property Beige As Color
            Get
                Return VgsNamed("beige")
            End Get
        End Property
        Public Shared ReadOnly Property Bisque As Color
            Get
                Return VgsNamed("bisque")
            End Get
        End Property
        Public Shared ReadOnly Property BlanchedAlmond As Color
            Get
                Return VgsNamed("blanchedalmond")
            End Get
        End Property
        Public Shared ReadOnly Property BlueViolet As Color
            Get
                Return VgsNamed("blueviolet")
            End Get
        End Property
        Public Shared ReadOnly Property Brown As Color
            Get
                Return VgsNamed("brown")
            End Get
        End Property
        Public Shared ReadOnly Property BurlyWood As Color
            Get
                Return VgsNamed("burlywood")
            End Get
        End Property
        Public Shared ReadOnly Property CadetBlue As Color
            Get
                Return VgsNamed("cadetblue")
            End Get
        End Property
        Public Shared ReadOnly Property Chartreuse As Color
            Get
                Return VgsNamed("chartreuse")
            End Get
        End Property
        Public Shared ReadOnly Property Chocolate As Color
            Get
                Return VgsNamed("chocolate")
            End Get
        End Property
        Public Shared ReadOnly Property Coral As Color
            Get
                Return VgsNamed("coral")
            End Get
        End Property
        Public Shared ReadOnly Property CornflowerBlue As Color
            Get
                Return VgsNamed("cornflowerblue")
            End Get
        End Property
        Public Shared ReadOnly Property Cornsilk As Color
            Get
                Return VgsNamed("cornsilk")
            End Get
        End Property
        Public Shared ReadOnly Property Crimson As Color
            Get
                Return VgsNamed("crimson")
            End Get
        End Property
        Public Shared ReadOnly Property Cyan As Color
            Get
                Return VgsNamed("cyan")
            End Get
        End Property
        Public Shared ReadOnly Property DarkBlue As Color
            Get
                Return VgsNamed("darkblue")
            End Get
        End Property
        Public Shared ReadOnly Property DarkCyan As Color
            Get
                Return VgsNamed("darkcyan")
            End Get
        End Property
        Public Shared ReadOnly Property DarkGoldenrod As Color
            Get
                Return VgsNamed("darkgoldenrod")
            End Get
        End Property
        Public Shared ReadOnly Property DarkGray As Color
            Get
                Return VgsNamed("darkgray")
            End Get
        End Property
        Public Shared ReadOnly Property DarkGreen As Color
            Get
                Return VgsNamed("darkgreen")
            End Get
        End Property
        Public Shared ReadOnly Property DarkKhaki As Color
            Get
                Return VgsNamed("darkkhaki")
            End Get
        End Property
        Public Shared ReadOnly Property DarkMagenta As Color
            Get
                Return VgsNamed("darkmagenta")
            End Get
        End Property
        Public Shared ReadOnly Property DarkOliveGreen As Color
            Get
                Return VgsNamed("darkolivegreen")
            End Get
        End Property
        Public Shared ReadOnly Property DarkOrange As Color
            Get
                Return VgsNamed("darkorange")
            End Get
        End Property
        Public Shared ReadOnly Property DarkOrchid As Color
            Get
                Return VgsNamed("darkorchid")
            End Get
        End Property
        Public Shared ReadOnly Property DarkRed As Color
            Get
                Return VgsNamed("darkred")
            End Get
        End Property
        Public Shared ReadOnly Property DarkSalmon As Color
            Get
                Return VgsNamed("darksalmon")
            End Get
        End Property
        Public Shared ReadOnly Property DarkSeaGreen As Color
            Get
                Return VgsNamed("darkseagreen")
            End Get
        End Property
        Public Shared ReadOnly Property DarkSlateBlue As Color
            Get
                Return VgsNamed("darkslateblue")
            End Get
        End Property
        Public Shared ReadOnly Property DarkSlateGray As Color
            Get
                Return VgsNamed("darkslategray")
            End Get
        End Property
        Public Shared ReadOnly Property DarkTurquoise As Color
            Get
                Return VgsNamed("darkturquoise")
            End Get
        End Property
        Public Shared ReadOnly Property DarkViolet As Color
            Get
                Return VgsNamed("darkviolet")
            End Get
        End Property
        Public Shared ReadOnly Property DeepPink As Color
            Get
                Return VgsNamed("deeppink")
            End Get
        End Property
        Public Shared ReadOnly Property DeepSkyBlue As Color
            Get
                Return VgsNamed("deepskyblue")
            End Get
        End Property
        Public Shared ReadOnly Property DimGray As Color
            Get
                Return VgsNamed("dimgray")
            End Get
        End Property
        Public Shared ReadOnly Property DodgerBlue As Color
            Get
                Return VgsNamed("dodgerblue")
            End Get
        End Property
        Public Shared ReadOnly Property Firebrick As Color
            Get
                Return VgsNamed("firebrick")
            End Get
        End Property
        Public Shared ReadOnly Property FloralWhite As Color
            Get
                Return VgsNamed("floralwhite")
            End Get
        End Property
        Public Shared ReadOnly Property ForestGreen As Color
            Get
                Return VgsNamed("forestgreen")
            End Get
        End Property
        Public Shared ReadOnly Property Fuchsia As Color
            Get
                Return VgsNamed("fuchsia")
            End Get
        End Property
        Public Shared ReadOnly Property Gainsboro As Color
            Get
                Return VgsNamed("gainsboro")
            End Get
        End Property
        Public Shared ReadOnly Property GhostWhite As Color
            Get
                Return VgsNamed("ghostwhite")
            End Get
        End Property
        Public Shared ReadOnly Property Gold As Color
            Get
                Return VgsNamed("gold")
            End Get
        End Property
        Public Shared ReadOnly Property Goldenrod As Color
            Get
                Return VgsNamed("goldenrod")
            End Get
        End Property
        Public Shared ReadOnly Property GreenYellow As Color
            Get
                Return VgsNamed("greenyellow")
            End Get
        End Property
        Public Shared ReadOnly Property Honeydew As Color
            Get
                Return VgsNamed("honeydew")
            End Get
        End Property
        Public Shared ReadOnly Property HotPink As Color
            Get
                Return VgsNamed("hotpink")
            End Get
        End Property
        Public Shared ReadOnly Property IndianRed As Color
            Get
                Return VgsNamed("indianred")
            End Get
        End Property
        Public Shared ReadOnly Property Indigo As Color
            Get
                Return VgsNamed("indigo")
            End Get
        End Property
        Public Shared ReadOnly Property Ivory As Color
            Get
                Return VgsNamed("ivory")
            End Get
        End Property
        Public Shared ReadOnly Property Khaki As Color
            Get
                Return VgsNamed("khaki")
            End Get
        End Property
        Public Shared ReadOnly Property Lavender As Color
            Get
                Return VgsNamed("lavender")
            End Get
        End Property
        Public Shared ReadOnly Property LavenderBlush As Color
            Get
                Return VgsNamed("lavenderblush")
            End Get
        End Property
        Public Shared ReadOnly Property LawnGreen As Color
            Get
                Return VgsNamed("lawngreen")
            End Get
        End Property
        Public Shared ReadOnly Property LemonChiffon As Color
            Get
                Return VgsNamed("lemonchiffon")
            End Get
        End Property
        Public Shared ReadOnly Property LightBlue As Color
            Get
                Return VgsNamed("lightblue")
            End Get
        End Property
        Public Shared ReadOnly Property LightCoral As Color
            Get
                Return VgsNamed("lightcoral")
            End Get
        End Property
        Public Shared ReadOnly Property LightCyan As Color
            Get
                Return VgsNamed("lightcyan")
            End Get
        End Property
        Public Shared ReadOnly Property LightGoldenrodYellow As Color
            Get
                Return VgsNamed("lightgoldenrodyellow")
            End Get
        End Property
        Public Shared ReadOnly Property LightGray As Color
            Get
                Return VgsNamed("lightgray")
            End Get
        End Property
        Public Shared ReadOnly Property LightGreen As Color
            Get
                Return VgsNamed("lightgreen")
            End Get
        End Property
        Public Shared ReadOnly Property LightPink As Color
            Get
                Return VgsNamed("lightpink")
            End Get
        End Property
        Public Shared ReadOnly Property LightSalmon As Color
            Get
                Return VgsNamed("lightsalmon")
            End Get
        End Property
        Public Shared ReadOnly Property LightSeaGreen As Color
            Get
                Return VgsNamed("lightseagreen")
            End Get
        End Property
        Public Shared ReadOnly Property LightSkyBlue As Color
            Get
                Return VgsNamed("lightskyblue")
            End Get
        End Property
        Public Shared ReadOnly Property LightSlateGray As Color
            Get
                Return VgsNamed("lightslategray")
            End Get
        End Property
        Public Shared ReadOnly Property LightSteelBlue As Color
            Get
                Return VgsNamed("lightsteelblue")
            End Get
        End Property
        Public Shared ReadOnly Property LightYellow As Color
            Get
                Return VgsNamed("lightyellow")
            End Get
        End Property
        Public Shared ReadOnly Property Lime As Color
            Get
                Return VgsNamed("lime")
            End Get
        End Property
        Public Shared ReadOnly Property LimeGreen As Color
            Get
                Return VgsNamed("limegreen")
            End Get
        End Property
        Public Shared ReadOnly Property Linen As Color
            Get
                Return VgsNamed("linen")
            End Get
        End Property
        Public Shared ReadOnly Property Magenta As Color
            Get
                Return VgsNamed("magenta")
            End Get
        End Property
        Public Shared ReadOnly Property Maroon As Color
            Get
                Return VgsNamed("maroon")
            End Get
        End Property
        Public Shared ReadOnly Property MediumAquamarine As Color
            Get
                Return VgsNamed("mediumaquamarine")
            End Get
        End Property
        Public Shared ReadOnly Property MediumBlue As Color
            Get
                Return VgsNamed("mediumblue")
            End Get
        End Property
        Public Shared ReadOnly Property MediumOrchid As Color
            Get
                Return VgsNamed("mediumorchid")
            End Get
        End Property
        Public Shared ReadOnly Property MediumPurple As Color
            Get
                Return VgsNamed("mediumpurple")
            End Get
        End Property
        Public Shared ReadOnly Property MediumSeaGreen As Color
            Get
                Return VgsNamed("mediumseagreen")
            End Get
        End Property
        Public Shared ReadOnly Property MediumSlateBlue As Color
            Get
                Return VgsNamed("mediumslateblue")
            End Get
        End Property
        Public Shared ReadOnly Property MediumSpringGreen As Color
            Get
                Return VgsNamed("mediumspringgreen")
            End Get
        End Property
        Public Shared ReadOnly Property MediumTurquoise As Color
            Get
                Return VgsNamed("mediumturquoise")
            End Get
        End Property
        Public Shared ReadOnly Property MediumVioletRed As Color
            Get
                Return VgsNamed("mediumvioletred")
            End Get
        End Property
        Public Shared ReadOnly Property MidnightBlue As Color
            Get
                Return VgsNamed("midnightblue")
            End Get
        End Property
        Public Shared ReadOnly Property MintCream As Color
            Get
                Return VgsNamed("mintcream")
            End Get
        End Property
        Public Shared ReadOnly Property MistyRose As Color
            Get
                Return VgsNamed("mistyrose")
            End Get
        End Property
        Public Shared ReadOnly Property Moccasin As Color
            Get
                Return VgsNamed("moccasin")
            End Get
        End Property
        Public Shared ReadOnly Property NavajoWhite As Color
            Get
                Return VgsNamed("navajowhite")
            End Get
        End Property
        Public Shared ReadOnly Property Navy As Color
            Get
                Return VgsNamed("navy")
            End Get
        End Property
        Public Shared ReadOnly Property OldLace As Color
            Get
                Return VgsNamed("oldlace")
            End Get
        End Property
        Public Shared ReadOnly Property Olive As Color
            Get
                Return VgsNamed("olive")
            End Get
        End Property
        Public Shared ReadOnly Property OliveDrab As Color
            Get
                Return VgsNamed("olivedrab")
            End Get
        End Property
        Public Shared ReadOnly Property Orange As Color
            Get
                Return VgsNamed("orange")
            End Get
        End Property
        Public Shared ReadOnly Property OrangeRed As Color
            Get
                Return VgsNamed("orangered")
            End Get
        End Property
        Public Shared ReadOnly Property Orchid As Color
            Get
                Return VgsNamed("orchid")
            End Get
        End Property
        Public Shared ReadOnly Property PaleGoldenrod As Color
            Get
                Return VgsNamed("palegoldenrod")
            End Get
        End Property
        Public Shared ReadOnly Property PaleGreen As Color
            Get
                Return VgsNamed("palegreen")
            End Get
        End Property
        Public Shared ReadOnly Property PaleTurquoise As Color
            Get
                Return VgsNamed("paleturquoise")
            End Get
        End Property
        Public Shared ReadOnly Property PaleVioletRed As Color
            Get
                Return VgsNamed("palevioletred")
            End Get
        End Property
        Public Shared ReadOnly Property PapayaWhip As Color
            Get
                Return VgsNamed("papayawhip")
            End Get
        End Property
        Public Shared ReadOnly Property PeachPuff As Color
            Get
                Return VgsNamed("peachpuff")
            End Get
        End Property
        Public Shared ReadOnly Property Peru As Color
            Get
                Return VgsNamed("peru")
            End Get
        End Property
        Public Shared ReadOnly Property Pink As Color
            Get
                Return VgsNamed("pink")
            End Get
        End Property
        Public Shared ReadOnly Property Plum As Color
            Get
                Return VgsNamed("plum")
            End Get
        End Property
        Public Shared ReadOnly Property PowderBlue As Color
            Get
                Return VgsNamed("powderblue")
            End Get
        End Property
        Public Shared ReadOnly Property Purple As Color
            Get
                Return VgsNamed("purple")
            End Get
        End Property
        Public Shared ReadOnly Property RosyBrown As Color
            Get
                Return VgsNamed("rosybrown")
            End Get
        End Property
        Public Shared ReadOnly Property RoyalBlue As Color
            Get
                Return VgsNamed("royalblue")
            End Get
        End Property
        Public Shared ReadOnly Property SaddleBrown As Color
            Get
                Return VgsNamed("saddlebrown")
            End Get
        End Property
        Public Shared ReadOnly Property Salmon As Color
            Get
                Return VgsNamed("salmon")
            End Get
        End Property
        Public Shared ReadOnly Property SandyBrown As Color
            Get
                Return VgsNamed("sandybrown")
            End Get
        End Property
        Public Shared ReadOnly Property SeaGreen As Color
            Get
                Return VgsNamed("seagreen")
            End Get
        End Property
        Public Shared ReadOnly Property SeaShell As Color
            Get
                Return VgsNamed("seashell")
            End Get
        End Property
        Public Shared ReadOnly Property Sienna As Color
            Get
                Return VgsNamed("sienna")
            End Get
        End Property
        Public Shared ReadOnly Property Silver As Color
            Get
                Return VgsNamed("silver")
            End Get
        End Property
        Public Shared ReadOnly Property SkyBlue As Color
            Get
                Return VgsNamed("skyblue")
            End Get
        End Property
        Public Shared ReadOnly Property SlateBlue As Color
            Get
                Return VgsNamed("slateblue")
            End Get
        End Property
        Public Shared ReadOnly Property SlateGray As Color
            Get
                Return VgsNamed("slategray")
            End Get
        End Property
        Public Shared ReadOnly Property Snow As Color
            Get
                Return VgsNamed("snow")
            End Get
        End Property
        Public Shared ReadOnly Property SpringGreen As Color
            Get
                Return VgsNamed("springgreen")
            End Get
        End Property
        Public Shared ReadOnly Property SteelBlue As Color
            Get
                Return VgsNamed("steelblue")
            End Get
        End Property
        Public Shared ReadOnly Property Tan As Color
            Get
                Return VgsNamed("tan")
            End Get
        End Property
        Public Shared ReadOnly Property Teal As Color
            Get
                Return VgsNamed("teal")
            End Get
        End Property
        Public Shared ReadOnly Property Thistle As Color
            Get
                Return VgsNamed("thistle")
            End Get
        End Property
        Public Shared ReadOnly Property Tomato As Color
            Get
                Return VgsNamed("tomato")
            End Get
        End Property
        Public Shared ReadOnly Property Turquoise As Color
            Get
                Return VgsNamed("turquoise")
            End Get
        End Property
        Public Shared ReadOnly Property Violet As Color
            Get
                Return VgsNamed("violet")
            End Get
        End Property
        Public Shared ReadOnly Property Wheat As Color
            Get
                Return VgsNamed("wheat")
            End Get
        End Property
        Public Shared ReadOnly Property WhiteSmoke As Color
            Get
                Return VgsNamed("whitesmoke")
            End Get
        End Property
        Public Shared ReadOnly Property Yellow As Color
            Get
                Return VgsNamed("yellow")
            End Get
        End Property
        Public Shared ReadOnly Property YellowGreen As Color
            Get
                Return VgsNamed("yellowgreen")
            End Get
        End Property


        Public Function VgsCss() As String
            If Me.Name <> "" Then Return Me.Name
            If Me.A = 0 Then Return "transparent"
            Return "rgb(" & Me.R & "," & Me.G & "," & Me.B & ")"
        End Function
    End Class

    Public Class Point
        Public Property X As Integer
        Public Property Y As Integer

        Public Sub New(x As Integer, y As Integer)
            Me.X = x
            Me.Y = y
        End Sub
    End Class

    Public Class Size
        Public Property Width As Integer
        Public Property Height As Integer

        Public Sub New(width As Integer, height As Integer)
            Me.Width = width
            Me.Height = height
        End Sub
    End Class

    Public Class Image
    End Class

    Public Class Font
        Public Property Name As String
        Public Property Size As Double
        Public Property Bold As Boolean
        Public Property Italic As Boolean
        Public Property Underline As Boolean
        Public Property Strikeout As Boolean


        Public Function Equals(other As Font) As Boolean
            If other Is Nothing Then Return False
            Return Me.Name = other.Name AndAlso Me.Size = other.Size AndAlso Me.Bold = other.Bold AndAlso Me.Italic = other.Italic AndAlso Me.Underline = other.Underline AndAlso Me.Strikeout = other.Strikeout
        End Function


    End Class

    Public Enum FontStyle
        Regular = 0
        Bold = 1
        Italic = 2
        Underline = 4
        Strikeout = 8
    End Enum
End Namespace
