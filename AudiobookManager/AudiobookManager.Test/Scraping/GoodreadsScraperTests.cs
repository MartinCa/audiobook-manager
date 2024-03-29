using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Scraping;

[TestClass]
public class GoodreadsScraperTests
{
    [TestMethod]
    public async Task ParseNewBookJson()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var bookSeriesMapper = new Mock<IBookSeriesMapper>();
        bookSeriesMapper.Setup(x => x.MapBookSeries(It.IsAny<IList<BookSeriesSearchResult>>())).Returns<IList<BookSeriesSearchResult>>(x => Task.FromResult(x));
        var logger = new Mock<ILogger<GoodreadsScraper>>();

        var target = new GoodreadsScraper(httpClientFactory.Object, bookSeriesMapper.Object, logger.Object);

        var jsonText = @"{""props"":{""pageProps"":{""jwtToken"":""eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6ImZSNXpfWTVjYXZQMllsaXU3eks0YUNJVEJPcVBWdGtxTE9XVURfV3dGOTQifQ.eyJpc3MiOiJodHRwczovL3d3dy5nb29kcmVhZHMuY29tIiwic3ViIjoia2NhOi8vcHJvZmlsZTpnb29kcmVhZHMvQTNRQVhUTkxQM0I5M1UiLCJhdWQiOiI2M2RjMjRlN2M2MTFlNmYxNzkyZjgxMzA1OGYyMTU2MGJkOGM2OTM4ZDU0YS5nb29kcmVhZHMuY29tIiwidXNlcl9pZCI6NDA0NjM5OCwicm9sZSI6ImxpYnJhcmlhbiIsIm5vbmNlIjpudWxsLCJleHAiOjE2NzA0MTM0OTksImlhdCI6MTY3MDQxMzE5OX0.IMrPYVk7F43ap9U_5ginb4l9QyyXF0_ZHP6bKXfGxdDhi9qkwJlSjv6dvjxOXCajIw0BSUpkMzXxDxxqCMlkiXm29Xg9wp-AirmwX5VeveKvH4Z2r_bLRkVROgP07ZHMU_jFNQj6ooRXJd7DOwm5KrQN5Ij2cjnbV0nE0OZV_fB-ZpIibhz2Cyz3npClMNwqn7G0pt9188f5xjimY00KNTqfI-k4smr4KaeiUHFnZo0X9pahm0Ccl60ladJSA8nbhK_XCMPegom_6XkdP4pJwHb6jfJRuZC0rKl05ArX6DZ36IG1kgS36Iul_RgqvxQTTlIdKhuwOD8_2YgZOnYLJw"",""apolloState"":{""ROOT_QUERY"":{""__typename"":""Query"",""getSiteHeaderBanner"":{""__typename"":""SiteHeaderBanner"",""altText"":null,""clickthroughUrl"":null,""desktop1xPhoto"":null,""desktop2xPhoto"":null,""mobile1xPhoto"":null,""mobile2xPhoto"":null,""siteStripColor"":null},""getAdsTargeting({\""getAdsTargetingInput\"":{\""contextual\"":{\""legacyId\"":\""3111609\"",\""legacyResourceType\"":\""book\""}}})"":{""__typename"":""AdsTargeting"",""behavioral"":{""__typename"":""BehavioralTargeting"",""adGroupNames"":[],""age"":""35-39"",""authors"":[""1069006"",""38550"",""12577"",""625"",""721"",""589"",""1239"",""17061"",""7779"",""880695"",""7358493"",""9629"",""12605"",""12130438"",""32699"",""8337289"",""15179122"",""2063919"",""14640243"",""2476"",""14954351"",""903"",""33467"",""5141609"",""19025070"",""6333"",""1027169"",""92597"",""548923"",""14851776"",""106444"",""735413"",""2961590"",""3078"",""21101646"",""346745"",""7280714"",""2637"",""328437"",""318719"",""22645"",""145783"",""12076"",""3119987"",""6988"",""10765"",""858"",""52310"",""575"",""15619""],""blockedAuthors"":[],""gender"":""male"",""genres"":[""1"",""64"",""144"",""107"",""411"",""67"",""69"",""97"",""584"",""2286"",""552"",""24"",""836"",""5"",""2352"",""244"",""28"",""35"",""40"",""1049"",""1679"",""48"",""1560"",""84"",""128"",""2515"",""2207"",""29"",""8263"",""291"",""136"",""352"",""1860"",""1105"",""22643"",""384"",""5737"",""355"",""2021"",""117"",""127"",""25"",""72"",""68"",""266"",""96"",""58"",""1651"",""773"",""2795""],""userTargetingSettings"":{""__typename"":""UserTargetingSettings"",""allowBehavioralTargeting"":true}},""contextual"":{""__typename"":""ContextualTargeting"",""resource"":""Work_6976108"",""tags"":[""1"",""67"",""69"",""584"",""552"",""836"",""84"",""16729"",""244"",""107""],""adult"":false}},""getBookByLegacyId({\""legacyId\"":\""3111609\""})"":{""__ref"":""Book:kca://book/amzn1.gr.book.v1.D8KdYynU_O_zcE1Jns8J6w""},""getPageBanner({\""getPageBannerInput\"":{\""id\"":\""kca://book/amzn1.gr.book.v1.D8KdYynU_O_zcE1Jns8J6w\"",\""pageName\"":\""book_show\""}})"":{""__typename"":""PageBanner"",""type"":null,""message"":null},""getBook({\""id\"":\""kca://book/amzn1.gr.book.v1.f-RHfYvLTigupj-1WQiRPw\""})"":{""__ref"":""Book:kca://book/amzn1.gr.book.v1.f-RHfYvLTigupj-1WQiRPw""}},""Contributor:kca://author/amzn1.gr.author.v1.rKRpMFO6_9EstSR7vce5fg"":{""id"":""kca://author/amzn1.gr.author.v1.rKRpMFO6_9EstSR7vce5fg"",""__typename"":""Contributor"",""legacyId"":706255,""name"":""Stieg Larsson"",""description"":""\u003cb\u003eStieg Larsson\u003c/b\u003e (born as \u003cb\u003eKarl Stig-Erland Larsson\u003c/b\u003e) was a Swedish journalist and writer who passed away in 2004.\u003cbr /\u003e\u003cbr /\u003eAs a journalist and editor of the magazine \u003ci\u003e\n  \u003ca href=\""http://expo.se/\"" rel=\""nofollow noopener\""\u003eExpo\u003c/a\u003e\n\u003c/i\u003e, Larsson was active in documenting and exposing Swedish extreme right and racist organisations. When he died at the age of 50, Larsson left three unpublished thrillers and unfinished manuscripts for more. The first three books (\u003ci\u003e\n  \u003ca href=\""http://www.goodreads.com/book/show/2429135.The_Girl_With_the_Dragon_Tattoo\"" rel=\""nofollow noopener\""\u003eThe Girl With the Dragon Tattoo\u003c/a\u003e\n\u003c/i\u003e, \u003ci\u003e\n  \u003ca href=\""http://www.goodreads.com/book/show/5060378-the-girl-who-played-with-fire\"" rel=\""nofollow noopener\""\u003eThe Girl Who Played With Fire\u003c/a\u003e\n\u003c/i\u003e and \u003ci\u003e\n  \u003ca href=\""http://www.goodreads.com/book/show/6892870-the-girl-who-kicked-the-hornet-s-nest\"" rel=\""nofollow noopener\""\u003eThe Girl Who Kicked the Hornets' Nest\u003c/a\u003e\n\u003c/i\u003e) have since been printed as the \u003ci\u003e\n  \u003ca href=\""http://www.goodreads.com/series/44598-millennium\"" rel=\""nofollow noopener\""\u003eMillenium\u003c/a\u003e\n\u003c/i\u003e series. These books are all bestsellers in Sweden and in several other countries, including the United States and Canada.\u003cbr /\u003e\u003cbr /\u003eWitnessed a rape when he was 15, and was helpless to stop it. This event haunted him for the rest of his life. The girl being raped was named Lisbeth, which he later used as the name of the heroine on his Millenium trilogy. Sexual violence against women is also a recurring theme in his work.\u003cbr /\u003e\u003cbr /\u003ePersonal quote:\u003cbr /\u003eTo exact revenge for yourself or your friends is not only a right, it's an absolute duty."",""isGrAuthor"":false,""works"":{""__typename"":""ContributorWorksConnection"",""totalCount"":68},""profileImageUrl"":""https://i.gr-assets.com/images/S/compressed.photo.goodreads.com/authors/1595150953i/706255._UX200_CR0,0,200,200_.jpg"",""webUrl"":""https://www.goodreads.com/author/show/706255.Stieg_Larsson"",""user"":null,""viewerIsFollowing"":null,""followers"":{""__typename"":""ContributorFollowersConnection"",""totalCount"":14925}},""Contributor:kca://author/amzn1.gr.author.v1.O8mjKQoMgemz_6QMx7ax7Q"":{""id"":""kca://author/amzn1.gr.author.v1.O8mjKQoMgemz_6QMx7ax7Q"",""__typename"":""Contributor"",""name"":""Kamilla J�rgensen"",""webUrl"":""https://www.goodreads.com/author/show/5825224.Kamilla_J_rgensen"",""isGrAuthor"":false},""Series:kca://series/amzn1.gr.series.v1.mlpGWgIFEuiE2dmVLw-xnQ"":{""id"":""kca://series/amzn1.gr.series.v1.mlpGWgIFEuiE2dmVLw-xnQ"",""__typename"":""Series"",""title"":""Millennium"",""webUrl"":""https://www.goodreads.com/series/44598-millennium""},""Series:kca://series/amzn1.gr.series.v1.zHnV_wEZh4yO1VNV5VgXyQ"":{""id"":""kca://series/amzn1.gr.series.v1.zHnV_wEZh4yO1VNV5VgXyQ"",""__typename"":""Series"",""title"":""Millennium Split-Volume Edition"",""webUrl"":""https://www.goodreads.com/series/224601-millennium-split-volume-edition""},""Book:kca://book/amzn1.gr.book.v1.8xBlo8yOrbvliwJaH5-Pfg"":{""id"":""kca://book/amzn1.gr.book.v1.8xBlo8yOrbvliwJaH5-Pfg"",""__typename"":""Book"",""legacyId"":5060378,""webUrl"":""https://www.goodreads.com/book/show/5060378-the-girl-who-played-with-fire""},""Book:kca://book/amzn1.gr.book.v1.f-RHfYvLTigupj-1WQiRPw"":{""id"":""kca://book/amzn1.gr.book.v1.f-RHfYvLTigupj-1WQiRPw"",""__typename"":""Book"",""legacyId"":8645483,""reviewEditUrl"":""https://www.goodreads.com/review/edit/8645483""},""User:4046398"":{""id"":4046398,""__typename"":""User"",""legacyId"":4046398,""followersCount"":1,""imageUrlSquare"":""https://i.gr-assets.com/images/S/compressed.photo.goodreads.com/users/1280084340i/4046398._UX200_CR0,33,200,200_.jpg"",""isAuthor"":false,""textReviewsCount"":11,""name"":""Martin"",""webUrl"":""https://www.goodreads.com/user/show/4046398-martin""},""Review:kca://review:goodreads/amzn1.gr.review:goodreads.v1.W2MxAH4t2-8fHOeGCyl5OA"":{""id"":""kca://review:goodreads/amzn1.gr.review:goodreads.v1.W2MxAH4t2-8fHOeGCyl5OA"",""__typename"":""Review"",""creator"":{""__ref"":""User:4046398""},""viewerHasLiked"":false,""likeCount"":0,""comments"":{""__typename"":""ResourceCommentsConnection"",""totalCount"":0},""updatedAt"":1470424970875,""text"":""I really enjoyed reading this series of books, I bought the first one in the series after hearing a lot about it in the media. After finishing the first one I had to buy and read this one.\u003cbr /\u003e\u003cbr /\u003eI like the characters in the story and the sometimes surprising twists in the story."",""rating"":4},""Shelving:kca://profile:goodreads/A3QAXTNLP3B93U|kca://book/amzn1.gr.book.v1.f-RHfYvLTigupj-1WQiRPw"":{""id"":""kca://profile:goodreads/A3QAXTNLP3B93U|kca://book/amzn1.gr.book.v1.f-RHfYvLTigupj-1WQiRPw"",""__typename"":""Shelving"",""book"":{""__ref"":""Book:kca://book/amzn1.gr.book.v1.f-RHfYvLTigupj-1WQiRPw""},""shelf"":{""__typename"":""Shelf"",""name"":""read"",""webUrl"":""https://www.goodreads.com/review/list/4046398?shelf=read""},""taggings"":[{""__typename"":""Tagging"",""tag"":{""__typename"":""Tag"",""name"":""danish"",""webUrl"":""https://www.goodreads.com/review/list/4046398?shelf=danish""}}],""review"":{""__ref"":""Review:kca://review:goodreads/amzn1.gr.review:goodreads.v1.W2MxAH4t2-8fHOeGCyl5OA""},""webUrl"":""https://www.goodreads.com/review/show/113349441""},""Work:kca://work/amzn1.gr.work.v1.cUqcr7CL0D9wOrmWnKe0Ng"":{""id"":""kca://work/amzn1.gr.work.v1.cUqcr7CL0D9wOrmWnKe0Ng"",""__typename"":""Work"",""legacyId"":6976108,""bestBook"":{""__ref"":""Book:kca://book/amzn1.gr.book.v1.8xBlo8yOrbvliwJaH5-Pfg""},""choiceAwards"":[],""details"":{""__typename"":""WorkDetails"",""webUrl"":""https://www.goodreads.com/work/6976108-flickan-som-lekte-med-elden"",""shelvesUrl"":""https://www.goodreads.com/work/shelves/6976108-flickan-som-lekte-med-elden"",""publicationTime"":1149145200000,""originalTitle"":""Flickan som lekte med elden"",""awardsWon"":[{""__typename"":""Award"",""name"":""Anthony Award"",""webUrl"":""https://www.goodreads.com/award/show/145-anthony-award"",""awardedAt"":1262332800000,""category"":""Best Novel"",""hasWon"":false},{""__typename"":""Award"",""name"":""Dilys Award"",""webUrl"":""https://www.goodreads.com/award/show/923-dilys-award"",""awardedAt"":1262332800000,""category"":"""",""hasWon"":false},{""__typename"":""Award"",""name"":""CWA International Dagger"",""webUrl"":""https://www.goodreads.com/award/show/1114-cwa-international-dagger"",""awardedAt"":1230796800000,""category"":"""",""hasWon"":false},{""__typename"":""Award"",""name"":""Svenska Deckarakademins pris f�r b�sta svenska kriminalroman"",""webUrl"":""https://www.goodreads.com/award/show/14872-svenska-deckarakademins-pris-f-r-b-sta-svenska-kriminalroman"",""awardedAt"":1136102400000,""category"":null,""hasWon"":true},{""__typename"":""Award"",""name"":""Goodreads Choice Award"",""webUrl"":""https://www.goodreads.com/award/show/21332-goodreads-choice-award"",""awardedAt"":1230796800000,""category"":""Mystery/Thriller"",""hasWon"":true}],""places"":[{""__typename"":""Places"",""name"":""Stockholm"",""countryName"":""Sweden"",""webUrl"":""https://www.goodreads.com/places/1370-stockholm"",""year"":null},{""__typename"":""Places"",""name"":""Sweden"",""countryName"":null,""webUrl"":""https://www.goodreads.com/places/48-sweden"",""year"":null}],""characters"":[{""__typename"":""Character"",""name"":""Lisbeth Salander"",""webUrl"":""https://www.goodreads.com/characters/7800-lisbeth-salander""},{""__typename"":""Character"",""name"":""Mikael Blomkvist"",""webUrl"":""https://www.goodreads.com/characters/43091-mikael-blomkvist""},{""__typename"":""Character"",""name"":""Alexander Zalachenko"",""webUrl"":""https://www.goodreads.com/characters/51038-alexander-zalachenko""},{""__typename"":""Character"",""name"":""Jan Bublanski"",""webUrl"":""https://www.goodreads.com/characters/51039-jan-bublanski""},{""__typename"":""Character"",""name"":""Sonja Modig"",""webUrl"":""https://www.goodreads.com/characters/51040-sonja-modig""},{""__typename"":""Character"",""name"":""Peter Teleborian"",""webUrl"":""https://www.goodreads.com/characters/56644-peter-teleborian""},{""__typename"":""Character"",""name"":""Erika Berger"",""webUrl"":""https://www.goodreads.com/characters/56645-erika-berger""},{""__typename"":""Character"",""name"":""Ronald Niedermann"",""webUrl"":""https://www.goodreads.com/characters/56646-ronald-niedermann""},{""__typename"":""Character"",""name"":""Annika Giannini"",""webUrl"":""https://www.goodreads.com/characters/56647-annika-giannini""},{""__typename"":""Character"",""name"":""Dragan Armansky"",""webUrl"":""https://www.goodreads.com/characters/56648-dragan-armansky""},{""__typename"":""Character"",""name"":""Gunnar Bj�rck"",""webUrl"":""https://www.goodreads.com/characters/56652-gunnar-bj-rck""},{""__typename"":""Character"",""name"":""Harriet Vanger"",""webUrl"":""https://www.goodreads.com/characters/61060-harriet-vanger""},{""__typename"":""Character"",""name"":""Holger Palmgren"",""webUrl"":""https://www.goodreads.com/characters/61061-holger-palmgren""},{""__typename"":""Character"",""name"":""Nils Bjurman"",""webUrl"":""https://www.goodreads.com/characters/61062-nils-bjurman""},{""__typename"":""Character"",""name"":""Miriam Wu"",""webUrl"":""https://www.goodreads.com/characters/69893-miriam-wu""}]},""stats"":{""__typename"":""BookOrWorkStats"",""averageRating"":4.25,""ratingsCount"":879217,""ratingsCountDist"":[7074,20454,116507,339761,395421],""textReviewsCount"":35904,""textReviewsLanguageCounts"":[{""__typename"":""TextReviewLanguageCount"",""count"":32017,""isoLanguageCode"":""en""},{""__typename"":""TextReviewLanguageCount"",""count"":13,""isoLanguageCode"":""da""},{""__typename"":""TextReviewLanguageCount"",""count"":93,""isoLanguageCode"":""de""},{""__typename"":""TextReviewLanguageCount"",""count"":108,""isoLanguageCode"":""fr""},{""__typename"":""TextReviewLanguageCount"",""count"":2,""isoLanguageCode"":""zh""},{""__typename"":""TextReviewLanguageCount"",""count"":704,""isoLanguageCode"":""es""},{""__typename"":""TextReviewLanguageCount"",""count"":10,""isoLanguageCode"":""th""},{""__typename"":""TextReviewLanguageCount"",""count"":57,""isoLanguageCode"":""cs""},{""__typename"":""TextReviewLanguageCount"",""count"":181,""isoLanguageCode"":""pt""},{""__typename"":""TextReviewLanguageCount"",""count"":103,""isoLanguageCode"":""nl""},{""__typename"":""TextReviewLanguageCount"",""count"":32,""isoLanguageCode"":""id""},{""__typename"":""TextReviewLanguageCount"",""count"":29,""isoLanguageCode"":""bg""},{""__typename"":""TextReviewLanguageCount"",""count"":17,""isoLanguageCode"":""sk""},{""__typename"":""TextReviewLanguageCount"",""count"":13,""isoLanguageCode"":""lv""},{""__typename"":""TextReviewLanguageCount"",""count"":147,""isoLanguageCode"":""it""},{""__typename"":""TextReviewLanguageCount"",""count"":84,""isoLanguageCode"":""ar""},{""__typename"":""TextReviewLanguageCount"",""count"":15,""isoLanguageCode"":""ro""},{""__typename"":""TextReviewLanguageCount"",""count"":3,""isoLanguageCode"":""hu""},{""__typename"":""TextReviewLanguageCount"",""count"":4,""isoLanguageCode"":""hr""},{""__typename"":""TextReviewLanguageCount"",""count"":44,""isoLanguageCode"":""pl""},{""__typename"":""TextReviewLanguageCount"",""count"":6,""isoLanguageCode"":""ca""},{""__typename"":""TextReviewLanguageCount"",""count"":3,""isoLanguageCode"":""zh-TW""},{""__typename"":""TextReviewLanguageCount"",""count"":28,""isoLanguageCode"":""fi""},{""__typename"":""TextReviewLanguageCount"",""count"":45,""isoLanguageCode"":""tr""},{""__typename"":""TextReviewLanguageCount"",""count"":34,""isoLanguageCode"":""el""},{""__typename"":""TextReviewLanguageCount"",""count"":51,""isoLanguageCode"":""sv""},{""__typename"":""TextReviewLanguageCount"",""count"":3,""isoLanguageCode"":""af""},{""__typename"":""TextReviewLanguageCount"",""count"":24,""isoLanguageCode"":""ru""},{""__typename"":""TextReviewLanguageCount"",""count"":3,""isoLanguageCode"":""sl""},{""__typename"":""TextReviewLanguageCount"",""count"":8,""isoLanguageCode"":""vi""},{""__typename"":""TextReviewLanguageCount"",""count"":3,""isoLanguageCode"":""uk""},{""__typename"":""TextReviewLanguageCount"",""count"":7,""isoLanguageCode"":""ka""},{""__typename"":""TextReviewLanguageCount"",""count"":2,""isoLanguageCode"":""ko""},{""__typename"":""TextReviewLanguageCount"",""count"":6,""isoLanguageCode"":""et""},{""__typename"":""TextReviewLanguageCount"",""count"":2,""isoLanguageCode"":""bn""},{""__typename"":""TextReviewLanguageCount"",""count"":1,""isoLanguageCode"":""mk""},{""__typename"":""TextReviewLanguageCount"",""count"":1,""isoLanguageCode"":""ml""},{""__typename"":""TextReviewLanguageCount"",""count"":1,""isoLanguageCode"":""az""},{""__typename"":""TextReviewLanguageCount"",""count"":1,""isoLanguageCode"":""mr""},{""__typename"":""TextReviewLanguageCount"",""count"":1,""isoLanguageCode"":""hy""},{""__typename"":""TextReviewLanguageCount"",""count"":5,""isoLanguageCode"":""no""},{""__typename"":""TextReviewLanguageCount"",""count"":1,""isoLanguageCode"":""he""},{""__typename"":""TextReviewLanguageCount"",""count"":7,""isoLanguageCode"":""lt""},{""__typename"":""TextReviewLanguageCount"",""count"":2,""isoLanguageCode"":""is""},{""__typename"":""TextReviewLanguageCount"",""count"":2,""isoLanguageCode"":""fa""}]},""quotes({\""pagination\"":{\""limit\"":1}})"":{""__typename"":""ResourceQuotesConnection"",""webUrl"":""https://www.goodreads.com/work/quotes/6976108"",""totalCount"":102},""questions({\""pagination\"":{\""limit\"":1}})"":{""__typename"":""ResourceQuestionsConnection"",""totalCount"":22,""webUrl"":""https://www.goodreads.com/book/5060378/questions""},""topics({\""pagination\"":{\""limit\"":1}})"":{""__typename"":""ResourceTopicsConnection"",""webUrl"":""https://www.goodreads.com/topic/list_book/5060378"",""totalCount"":47},""viewerShelvings"":[{""__ref"":""Shelving:kca://profile:goodreads/A3QAXTNLP3B93U|kca://book/amzn1.gr.book.v1.f-RHfYvLTigupj-1WQiRPw""}],""viewerShelvingsUrl"":""/review/user_works/6976108"",""featuredKNH"":{""__typename"":""FeaturedKNHCollectionConnection"",""totalCount"":0,""edges"":[]},""giveaways"":null,""editions"":{""__typename"":""BooksConnection"",""webUrl"":""https://www.goodreads.com/work/editions/6976108""}},""Book:kca://book/amzn1.gr.book.v1.D8KdYynU_O_zcE1Jns8J6w"":{""id"":""kca://book/amzn1.gr.book.v1.D8KdYynU_O_zcE1Jns8J6w"",""__typename"":""Book"",""title"":""Pigen der legede med ilden"",""titleComplete"":""Pigen der legede med ilden (Millennium, #2)"",""legacyId"":3111609,""webUrl"":""https://www.goodreads.com/book/show/3111609-pigen-der-legede-med-ilden"",""description"":""Mikael Blomkvist har f�et fingre i en varm nyhed. En ung journalist og hans forskerk�reste har afd�kket afg�rende oplysninger om en omfattende og voksende sexhandel mellem �steuropa og Sverige, hvis bagm�nd kommer fra de allerh�jeste samfundslag. I \""Pigen der legede med ilden\"" beder k�resteparret Millenium om at udgive en bog om deres opdagelser. En bog, der p� storsl�et vis vil afsl�re sandheden for offentligheden.\u003cbr /\u003e\u003cbr /\u003eMens Mikael efterforsker sammensv�rgelsen, stikker begivenheder i Lisbeth Salanders fortid hovedet frem. Begivenheder, der, efter et brutalt mord finder sted, retter mistanken mod den outrerede Lisbeth.\u003cbr /\u003e\u003cbr /\u003eP� trods af deres forskellige metoder, har Lisbeth og Mikael det samme m�l: at straffe dem, der har fortjent det."",""description({\""stripped\"":true})"":""Mikael Blomkvist har f�et fingre i en varm nyhed. En ung journalist og hans forskerk�reste har afd�kket afg�rende oplysninger om en omfattende og voksende sexhandel mellem �steuropa og Sverige, hvis bagm�nd kommer fra de allerh�jeste samfundslag. I \""Pigen der legede med ilden\"" beder k�resteparret Millenium om at udgive en bog om deres opdagelser. En bog, der p� storsl�et vis vil afsl�re sandheden for offentligheden.\r\n\r\nMens Mikael efterforsker sammensv�rgelsen, stikker begivenheder i Lisbeth Salanders fortid hovedet frem. Begivenheder, der, efter et brutalt mord finder sted, retter mistanken mod den outrerede Lisbeth.\r\n\r\nP� trods af deres forskellige metoder, har Lisbeth og Mikael det samme m�l: at straffe dem, der har fortjent det."",""primaryContributorEdge"":{""__typename"":""BookContributorEdge"",""node"":{""__ref"":""Contributor:kca://author/amzn1.gr.author.v1.rKRpMFO6_9EstSR7vce5fg""},""role"":""Author""},""secondaryContributorEdges"":[{""__typename"":""BookContributorEdge"",""node"":{""__ref"":""Contributor:kca://author/amzn1.gr.author.v1.O8mjKQoMgemz_6QMx7ax7Q""},""role"":""Translator""}],""imageUrl"":""https://images-na.ssl-images-amazon.com/images/S/compressed.photo.goodreads.com/books/1328116043i/3111609.jpg"",""bookSeries"":[{""__typename"":""BookSeries"",""userPosition"":""2"",""series"":{""__ref"":""Series:kca://series/amzn1.gr.series.v1.mlpGWgIFEuiE2dmVLw-xnQ""}},{""__typename"":""BookSeries"",""userPosition"":""3A"",""series"":{""__ref"":""Series:kca://series/amzn1.gr.series.v1.zHnV_wEZh4yO1VNV5VgXyQ""}}],""bookGenres"":[{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Fiction"",""webUrl"":""https://www.goodreads.com/genres/fiction""}},{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Mystery"",""webUrl"":""https://www.goodreads.com/genres/mystery""}},{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Thriller"",""webUrl"":""https://www.goodreads.com/genres/thriller""}},{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Crime"",""webUrl"":""https://www.goodreads.com/genres/crime""}},{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Mystery Thriller"",""webUrl"":""https://www.goodreads.com/genres/mystery-thriller""}},{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Suspense"",""webUrl"":""https://www.goodreads.com/genres/suspense""}},{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Contemporary"",""webUrl"":""https://www.goodreads.com/genres/contemporary""}},{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Sweden"",""webUrl"":""https://www.goodreads.com/genres/sweden""}},{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Adult"",""webUrl"":""https://www.goodreads.com/genres/adult""}},{""__typename"":""BookGenre"",""genre"":{""__typename"":""Genre"",""name"":""Audiobook"",""webUrl"":""https://www.goodreads.com/genres/audiobook""}}],""details"":{""__typename"":""BookDetails"",""asin"":""8770530149"",""format"":""Hardcover"",""numPages"":605,""publicationTime"":null,""publisher"":""Modtryk"",""isbn"":""8770530149"",""isbn13"":""9788770530149"",""language"":{""__typename"":""Language"",""name"":""Danish""}},""work"":{""__ref"":""Work:kca://work/amzn1.gr.work.v1.cUqcr7CL0D9wOrmWnKe0Ng""},""reviewEditUrl"":""https://www.goodreads.com/review/edit/3111609"",""featureFlags"":{""__typename"":""FeatureFlags"",""hideAds"":false,""isProtected"":false,""noIndex"":false},""viewerShelving"":{""__ref"":""Shelving:kca://profile:goodreads/A3QAXTNLP3B93U|kca://book/amzn1.gr.book.v1.D8KdYynU_O_zcE1Jns8J6w""},""links({})"":{""__typename"":""BookLinks"",""primaryAffiliateLink"":{""__typename"":""BookLink"",""name"":""Amazon"",""url"":""http://www.amazon.com/gp/product/8770530149/ref=x_gr_bb_amazon?ie=UTF8\u0026tag=x_gr_bb_amazon-20\u0026linkCode=as2\u0026camp=1789\u0026creative=9325\u0026creativeASIN=8770530149\u0026SubscriptionId=1MGPYB6YW3HWK55XCGG2"",""ref"":""x_gr_bb_amazon""},""secondaryAffiliateLinks"":[{""__typename"":""BookLink"",""name"":""Audible"",""url"":""https://www.amazon.com/s?k=Pigen+der+legede+med+ilden\u0026i=audible\u0026ref=x_gr_w_bb_audible-20\u0026tag=x_gr_w_bb_audible-20"",""ref"":""x_gr_bb_amazon""},{""__typename"":""BookLink"",""name"":""Barnes \u0026 Noble"",""url"":""https://www.barnesandnoble.com/w/?ean=9788770530149"",""ref"":null},{""__typename"":""BookLink"",""name"":""AbeBooks"",""url"":""http://affiliates.abebooks.com/c/64613/77416/2029?u=http%3A%2F%2Fwww.abebooks.com%2Fservlet%2FSearchResults%3Fisbn%3D8770530149"",""ref"":null},{""__typename"":""BookLink"",""name"":""Walmart eBooks"",""url"":""https://click.linksynergy.com/fs-bin/click?id=GwEz7vxblVU\u0026subid=\u0026offerid=361251.1\u0026type=10\u0026tmpid=9309\u0026u1=x_gr_w_bb\u0026RD_PARM1=https%3A%2F%2Fwww.kobo.com%2Fus%2Fen%2Fsearch%3FQuery%3D9788770530149"",""ref"":null},{""__typename"":""BookLink"",""name"":""Apple Books"",""url"":""https://geo.itunes.apple.com/us/book/isbn9788770530149?at=11lvdC\u0026mt=11\u0026ls=1"",""ref"":null},{""__typename"":""BookLink"",""name"":""Google Play"",""url"":""https://play.google.com/store/search?q=9788770530149\u0026c=books\u0026PCamrefID=bookpage\u0026PAffiliateID=10lHMS"",""ref"":null},{""__typename"":""BookLink"",""name"":""Book Depository"",""url"":""http://www.bookdepository.com/search?searchTerm=8770530149\u0026search=Find+book\u0026a_aid=goodreads"",""ref"":null},{""__typename"":""BookLink"",""name"":""Alibris"",""url"":""http://click.linksynergy.com/fs-bin/click?id=GwEz7vxblVU\u0026subid=\u0026offerid=189673.1\u0026type=10\u0026tmpid=939\u0026\u0026u1=x_gr_w_bb\u0026RD_PARM1=http%3A%2F%2Fwww.alibris.com%2Fbooksearch%3Fkeyword%3D8770530149"",""ref"":null},{""__typename"":""BookLink"",""name"":""Indigo"",""url"":""https://www.chapters.indigo.ca/en-ca/home/search/?keywords=9788770530149"",""ref"":null},{""__typename"":""BookLink"",""name"":""Better World Books"",""url"":""http://www.tkqlhce.com/ob117vpyvpxCFFFKMHLCEDHLGHKECEFMIGDFFHJDDD?url=http%3A%2F%2Fwww.betterworldbooks.com%2FPigen+der+legede+med+ilden-H0.aspx%3FSearchTerm%3D8770530149"",""ref"":null},{""__typename"":""BookLink"",""name"":""IndieBound"",""url"":""https://www.indiebound.org/book/9788770530149"",""ref"":null},{""__typename"":""BookLink"",""name"":""Thriftbooks"",""url"":""https://prf.hn/click/camref:1101ljNE7/pubref:8770530149/destination:https://www.thriftbooks.com/browse/?b.search=8770530149"",""ref"":null}],""libraryLinks"":[{""__typename"":""BookLink"",""name"":""Libraries"",""url"":""http://www.worldcat.org/isbn/8770530149?loc="",""ref"":null}],""overflowPageUrl"":""https://www.goodreads.com/book/3111609-pigen-der-legede-med-ilden/get_a_copy""}},""Shelving:kca://profile:goodreads/A3QAXTNLP3B93U|kca://book/amzn1.gr.book.v1.D8KdYynU_O_zcE1Jns8J6w"":{""id"":""kca://profile:goodreads/A3QAXTNLP3B93U|kca://book/amzn1.gr.book.v1.D8KdYynU_O_zcE1Jns8J6w"",""__typename"":""Shelving"",""book"":{""__ref"":""Book:kca://book/amzn1.gr.book.v1.D8KdYynU_O_zcE1Jns8J6w""},""shelf"":{""__typename"":""Shelf"",""name"":null,""webUrl"":null},""taggings"":[],""review"":null,""webUrl"":null}},""apolloClient"":null,""authContextParams"":{""signedIn"":true,""customerId"":""A3QAXTNLP3B93U"",""legacyCustomerId"":4046398,""role"":""librarian""},""userAgentContextParams"":{""isWebView"":false},""userAgent"":""Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:107.0) Gecko/20100101 Firefox/107.0""}},""page"":""/book/show/[book_id]"",""query"":{""ac"":""1"",""from_search"":""true"",""qid"":""9v2fmGP6cN"",""rank"":""1"",""book_id"":""3111609-pigen-der-legede-med-ilden""},""buildId"":""8p2JlqOir5bfCheOWsYPc"",""runtimeConfig"":{""env"":""Production""},""isFallback"":false,""customServer"":true,""gip"":true,""locales"":[""en"",""ab"",""aa"",""af"",""ak"",""sq"",""am"",""ar"",""an"",""hy"",""as"",""av"",""ae"",""ay"",""az"",""bm"",""ba"",""eu"",""be"",""bn"",""bh"",""bi"",""bs"",""br"",""bg"",""my"",""ca"",""ch"",""ce"",""ceb"",""ny"",""zh"",""zh-TW"",""cv"",""kw"",""co"",""cr"",""hr"",""cs"",""da"",""dv"",""nl"",""eo"",""et"",""ee"",""fo"",""fj"",""fi"",""fr"",""ff"",""gl"",""ka"",""de"",""el"",""gn"",""gu"",""ht"",""ha"",""he"",""hz"",""hi"",""ho"",""hu"",""ia"",""id"",""ie"",""ilo"",""ga"",""ig"",""ik"",""io"",""is"",""it"",""iu"",""ja"",""jv"",""kl"",""kn"",""kr"",""ks"",""kk"",""km"",""ki"",""rw"",""ky"",""kv"",""kg"",""ko"",""ku"",""kj"",""la"",""lb"",""lg"",""li"",""ln"",""lo"",""lt"",""lu"",""lv"",""gv"",""mk"",""mg"",""ms"",""ml"",""mt"",""mi"",""mr"",""mh"",""mn"",""na"",""nv"",""nb"",""nd"",""ne"",""new"",""ng"",""nn"",""no"",""ii"",""nr"",""oc"",""oj"",""cu"",""om"",""or"",""os"",""pa"",""pi"",""fa"",""pl"",""ps"",""pt"",""qu"",""rm"",""rn"",""ro"",""ru"",""sa"",""sc"",""sd"",""se"",""sm"",""sg"",""sr"",""gd"",""sn"",""si"",""sk"",""sl"",""so"",""st"",""es"",""su"",""sw"",""ss"",""sv"",""ta"",""te"",""tg"",""th"",""ti"",""bo"",""tk"",""tl"",""tn"",""to"",""tr"",""ts"",""tt"",""tw"",""ty"",""ug"",""uk"",""ur"",""uz"",""ve"",""vi"",""vo"",""wa"",""cy"",""wo"",""fy"",""xh"",""yi"",""yo"",""za""]}";

        var bookUrl = "https://www.goodreads.com/book/show/3111609-pigen-der-legede-med-ilden";

        var parseResult = await target.ParseNewBookJson(jsonText, bookUrl);

        Assert.IsNotNull(parseResult);

        // Name
        Assert.AreEqual("Pigen der legede med ilden", parseResult.BookName);

        // Url
        Assert.AreEqual(bookUrl, parseResult.Url);

        // Authors
        Assert.AreEqual(2, parseResult.Authors.Count());
        var author0 = parseResult.Authors.SingleOrDefault(x => x.Name == "Stieg Larsson");
        Assert.AreEqual("Author", author0.Role);
        var author1 = parseResult.Authors.SingleOrDefault(x => x.Name == "Kamilla J�rgensen");
        Assert.AreEqual("Translator", author1.Role);

        // Narrators
        Assert.AreEqual(0, parseResult.Narrators.Count());

        // Subtitle
        Assert.IsTrue(string.IsNullOrEmpty(parseResult.Subtitle));

        // Year
        Assert.AreEqual(2006, parseResult.Year);

        // ImageUrl
        Assert.AreEqual("https://images-na.ssl-images-amazon.com/images/S/compressed.photo.goodreads.com/books/1328116043i/3111609.jpg", parseResult.ImageUrl);

        // Series
        Assert.AreEqual(2, parseResult.Series.Count());
        var series0 = parseResult.Series.SingleOrDefault(x => x.SeriesName == "Millennium");
        Assert.AreEqual("2", series0.SeriesPart);
        var series1 = parseResult.Series.SingleOrDefault(x => x.SeriesName == "Millennium Split-Volume Edition");
        Assert.AreEqual("3A", series1.SeriesPart);

        // Description
        Assert.IsTrue(parseResult.Description.Contains("Blomkvist"));

        // Genres
        Assert.AreEqual(5, parseResult.Genres.Count());
        Assert.IsTrue(parseResult.Genres.Contains("Mystery"));

        // Rating
        Assert.IsTrue(Math.Abs(4.25 - parseResult.Rating.Value) <= 0.25);

        // NumberOfRatings
        Assert.IsTrue(parseResult.NumberOfRatings >= 879217);
    }
}
