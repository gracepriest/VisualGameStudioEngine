' Code-behind for the DOM-style Home page.
'
' A DOM form does not inherit Form: the pages runtime constructs this class,
' which wires the controls in InitializeComponent. Handlers are declared above
' the init region so the generated wiring can see them.

Public Class Home

    ' <vgs:designer region="controls" form="Home.blwebform" hash="a351638d">
    Private PageGrid As Element
    Private TopBar As Element
    Private TopBarText As Element
    Private TopBarNews As Element
    Private TopBarDevotions As Element
    Private TopBarSignIn As Element
    Private Header As Element
    Private Logo As Element
    Private NavRead As Element
    Private NavStudy As Element
    Private NavNews As Element
    Private NavExplore As Element
    Private NavStore As Element
    Private NavPlus As Element
    Private NavLogin As Element
    Private Promo As Element
    Private PromoText As Element
    Private PromoLink As Element
    Private Hero As Element
    Private HeroTitle As Element
    Private HeroSub As Element
    Private SearchBox As Element
    Private VersionBox As Element
    Private SearchButton As Element
    Private AdvancedLink As Element
    Private PlansLink As Element
    Private SearchHint As Element
    Private SearchStatus As Element
    Private Verse As Element
    Private VerseTitle As Element
    Private VerseOptions As Element
    Private VerseRef As Element
    Private VerseBody As Element
    Private VerseRead As Element
    Private VerseListen As Element
    Private VerseShare As Element
    Private StudySection As Element
    Private StudyTitle As Element
    Private StudyGrid As Element
    Private Card1 As Element
    Private Card1Kicker As Element
    Private Card1Title As Element
    Private Card1Body As Element
    Private Card1Link As Element
    Private Card2 As Element
    Private Card2Kicker As Element
    Private Card2Title As Element
    Private Card2Body As Element
    Private Card2Link As Element
    Private Card3 As Element
    Private Card3Kicker As Element
    Private Card3Title As Element
    Private Card3Body As Element
    Private Card3Link As Element
    Private ArticlesSection As Element
    Private ArticlesTitle As Element
    Private ArticlesAll As Element
    Private ArticleGrid As Element
    Private Art1 As Element
    Private Art1Image As Element
    Private Art1ImageLabel As Element
    Private Art1Title As Element
    Private Art1Body As Element
    Private Art1Link As Element
    Private Art2 As Element
    Private Art2Image As Element
    Private Art2ImageLabel As Element
    Private Art2Title As Element
    Private Art2Body As Element
    Private Art2Link As Element
    Private Art3 As Element
    Private Art3Image As Element
    Private Art3ImageLabel As Element
    Private Art3Title As Element
    Private Art3Body As Element
    Private Art3Link As Element
    Private News As Element
    Private NewsTitle As Element
    Private NewsSub As Element
    Private EmailBox As Element
    Private SubscribeButton As Element
    Private AgreeCheck As Element
    Private AgreeLabel As Element
    Private NewsStatus As Element
    Private Footer As Element
    Private FootBrand As Element
    Private FootTag As Element
    Private FootAbout As Element
    Private FootAbout1 As Element
    Private FootAbout2 As Element
    Private FootAbout3 As Element
    Private FootAbout4 As Element
    Private FootHelp As Element
    Private FootHelp1 As Element
    Private FootHelp2 As Element
    Private FootHelp3 As Element
    Private FootHelp4 As Element
    Private FootNetwork As Element
    Private FootNet1 As Element
    Private FootNet2 As Element
    Private FootNet3 As Element
    Private FootNet4 As Element
    Private FootPrefs As Element
    Private FootPref1 As Element
    Private FootPref2 As Element
    Private FootPref3 As Element
    Private FootLegal As Element
    ' </vgs:designer>

    Public Sub New()
        Me.InitializeComponent()
    End Sub

    Private Sub SearchButton_Click(e As DomEvent)
        Dim query As String = SearchBox.value
        If query.Length = 0 Then
            SearchStatus.textContent = "Type a passage, keyword, or topic first."
        Else
            SearchStatus.textContent = "Searching " & VersionBox.value & " for " & query & " (demo only)."
        End If
    End Sub

    Private Sub SubscribeButton_Click(e As DomEvent)
        Dim address As String = EmailBox.value
        Dim agreed As Boolean = AgreeCheck.checked

        If address.Length = 0 Then
            NewsStatus.textContent = "Enter an email address first."
        Else
            If Not agreed Then
                NewsStatus.textContent = "Please agree to receive email communications."
            Else
                NewsStatus.textContent = "Thanks — this demo would subscribe " & address & "."
            End If
        End If
    End Sub

    Private Sub VerseListen_Click(e As DomEvent)
        ::window.alert("Audio playback is not part of this mock.")
    End Sub

    Private Sub TopBar_Click(e As DomEvent)
        
    End Sub

    ' <vgs:designer region="init" form="Home.blwebform" hash="fe9f3e9e">
    Private Sub InitializeComponent()
        Dim doc As Document = ::document
        PageGrid = doc.getElementById("PageGrid")
        TopBar = doc.getElementById("TopBar")
        TopBarText = doc.getElementById("TopBarText")
        TopBarNews = doc.getElementById("TopBarNews")
        TopBarDevotions = doc.getElementById("TopBarDevotions")
        TopBarSignIn = doc.getElementById("TopBarSignIn")
        Header = doc.getElementById("Header")
        Logo = doc.getElementById("Logo")
        NavRead = doc.getElementById("NavRead")
        NavStudy = doc.getElementById("NavStudy")
        NavNews = doc.getElementById("NavNews")
        NavExplore = doc.getElementById("NavExplore")
        NavStore = doc.getElementById("NavStore")
        NavPlus = doc.getElementById("NavPlus")
        NavLogin = doc.getElementById("NavLogin")
        Promo = doc.getElementById("Promo")
        PromoText = doc.getElementById("PromoText")
        PromoLink = doc.getElementById("PromoLink")
        Hero = doc.getElementById("Hero")
        HeroTitle = doc.getElementById("HeroTitle")
        HeroSub = doc.getElementById("HeroSub")
        SearchBox = doc.getElementById("SearchBox")
        VersionBox = doc.getElementById("VersionBox")
        SearchButton = doc.getElementById("SearchButton")
        SearchButton.addEventListener("click", AddressOf SearchButton_Click)
        AdvancedLink = doc.getElementById("AdvancedLink")
        PlansLink = doc.getElementById("PlansLink")
        SearchHint = doc.getElementById("SearchHint")
        SearchStatus = doc.getElementById("SearchStatus")
        Verse = doc.getElementById("Verse")
        VerseTitle = doc.getElementById("VerseTitle")
        VerseOptions = doc.getElementById("VerseOptions")
        VerseRef = doc.getElementById("VerseRef")
        VerseBody = doc.getElementById("VerseBody")
        VerseRead = doc.getElementById("VerseRead")
        VerseListen = doc.getElementById("VerseListen")
        VerseListen.addEventListener("click", AddressOf VerseListen_Click)
        VerseShare = doc.getElementById("VerseShare")
        StudySection = doc.getElementById("StudySection")
        StudyTitle = doc.getElementById("StudyTitle")
        StudyGrid = doc.getElementById("StudyGrid")
        Card1 = doc.getElementById("Card1")
        Card1Kicker = doc.getElementById("Card1Kicker")
        Card1Title = doc.getElementById("Card1Title")
        Card1Body = doc.getElementById("Card1Body")
        Card1Link = doc.getElementById("Card1Link")
        Card2 = doc.getElementById("Card2")
        Card2Kicker = doc.getElementById("Card2Kicker")
        Card2Title = doc.getElementById("Card2Title")
        Card2Body = doc.getElementById("Card2Body")
        Card2Link = doc.getElementById("Card2Link")
        Card3 = doc.getElementById("Card3")
        Card3Kicker = doc.getElementById("Card3Kicker")
        Card3Title = doc.getElementById("Card3Title")
        Card3Body = doc.getElementById("Card3Body")
        Card3Link = doc.getElementById("Card3Link")
        ArticlesSection = doc.getElementById("ArticlesSection")
        ArticlesTitle = doc.getElementById("ArticlesTitle")
        ArticlesAll = doc.getElementById("ArticlesAll")
        ArticleGrid = doc.getElementById("ArticleGrid")
        Art1 = doc.getElementById("Art1")
        Art1Image = doc.getElementById("Art1Image")
        Art1ImageLabel = doc.getElementById("Art1ImageLabel")
        Art1Title = doc.getElementById("Art1Title")
        Art1Body = doc.getElementById("Art1Body")
        Art1Link = doc.getElementById("Art1Link")
        Art2 = doc.getElementById("Art2")
        Art2Image = doc.getElementById("Art2Image")
        Art2ImageLabel = doc.getElementById("Art2ImageLabel")
        Art2Title = doc.getElementById("Art2Title")
        Art2Body = doc.getElementById("Art2Body")
        Art2Link = doc.getElementById("Art2Link")
        Art3 = doc.getElementById("Art3")
        Art3Image = doc.getElementById("Art3Image")
        Art3ImageLabel = doc.getElementById("Art3ImageLabel")
        Art3Title = doc.getElementById("Art3Title")
        Art3Body = doc.getElementById("Art3Body")
        Art3Link = doc.getElementById("Art3Link")
        News = doc.getElementById("News")
        NewsTitle = doc.getElementById("NewsTitle")
        NewsSub = doc.getElementById("NewsSub")
        EmailBox = doc.getElementById("EmailBox")
        SubscribeButton = doc.getElementById("SubscribeButton")
        SubscribeButton.addEventListener("click", AddressOf SubscribeButton_Click)
        AgreeCheck = doc.getElementById("AgreeCheck")
        AgreeLabel = doc.getElementById("AgreeLabel")
        NewsStatus = doc.getElementById("NewsStatus")
        Footer = doc.getElementById("Footer")
        FootBrand = doc.getElementById("FootBrand")
        FootTag = doc.getElementById("FootTag")
        FootAbout = doc.getElementById("FootAbout")
        FootAbout1 = doc.getElementById("FootAbout1")
        FootAbout2 = doc.getElementById("FootAbout2")
        FootAbout3 = doc.getElementById("FootAbout3")
        FootAbout4 = doc.getElementById("FootAbout4")
        FootHelp = doc.getElementById("FootHelp")
        FootHelp1 = doc.getElementById("FootHelp1")
        FootHelp2 = doc.getElementById("FootHelp2")
        FootHelp3 = doc.getElementById("FootHelp3")
        FootHelp4 = doc.getElementById("FootHelp4")
        FootNetwork = doc.getElementById("FootNetwork")
        FootNet1 = doc.getElementById("FootNet1")
        FootNet2 = doc.getElementById("FootNet2")
        FootNet3 = doc.getElementById("FootNet3")
        FootNet4 = doc.getElementById("FootNet4")
        FootPrefs = doc.getElementById("FootPrefs")
        FootPref1 = doc.getElementById("FootPref1")
        FootPref2 = doc.getElementById("FootPref2")
        FootPref3 = doc.getElementById("FootPref3")
        FootLegal = doc.getElementById("FootLegal")
    End Sub
    ' </vgs:designer>
End Class
