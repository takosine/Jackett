using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jackett.Common.Models;
using Jackett.Common.Models.IndexerConfig;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils;
using Jackett.Common.Utils.Clients;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Jackett.Common.Indexers
{
    public class Digitalcore : BaseWebIndexer
    {
        private string SearchUrl { get { return SiteLink + "api/v1/torrents"; } }
        private string LoginUrl { get { return SiteLink + "api/v1/auth"; } }

        private new ConfigurationDataCookie configData
        {
            get { return (ConfigurationDataCookie)base.configData; }
            set { base.configData = value; }
        }


        public Digitalcore(IIndexerConfigurationService configService, WebClient w, Logger l, IProtectionService ps)
            : base(name: "DigitalCore",
                description: "DigitalCore is a Private Torrent Tracker for MOVIES / TV / GENERAL",
                link: "https://digitalcore.club/",
                caps: new TorznabCapabilities(),
                configService: configService,
                client: w,
                logger: l,
                p: ps,
                configData: new ConfigurationDataCookie())
        {
            Encoding = Encoding.UTF8;
            Language = "en-us";
            Type = "private";

            TorznabCaps.SupportsImdbMovieSearch = true;

            AddCategoryMapping(1, TorznabCatType.MoviesDVD, "Movies/DVDR");
            AddCategoryMapping(2, TorznabCatType.MoviesSD, "Movies/SD");
            AddCategoryMapping(3, TorznabCatType.MoviesBluRay, "Movies/BluRay");
            AddCategoryMapping(4, TorznabCatType.MoviesUHD, "Movies/4K");
            AddCategoryMapping(5, TorznabCatType.MoviesHD, "Movies/720p");
            AddCategoryMapping(6, TorznabCatType.MoviesHD, "Movies/1080p");
            AddCategoryMapping(7, TorznabCatType.MoviesHD, "Movies/PACKS");

            AddCategoryMapping(8, TorznabCatType.TVHD, "Tv/720p");
            AddCategoryMapping(9, TorznabCatType.TVHD, "Tv/1080p");
            AddCategoryMapping(10, TorznabCatType.TVSD, "Tv/SD");
            AddCategoryMapping(11, TorznabCatType.TVSD, "Tv/DVDR");
            AddCategoryMapping(12, TorznabCatType.TVHD, "Tv/PACKS");
            AddCategoryMapping(13, TorznabCatType.TVUHD, "Tv/4K");
            AddCategoryMapping(14, TorznabCatType.TVHD, "Tv/BluRay");

            AddCategoryMapping(17, TorznabCatType.Other, "Unknown");
            AddCategoryMapping(18, TorznabCatType.PC0day, "Apps/0day");
            AddCategoryMapping(20, TorznabCatType.PCISO, "Apps/PC");
            AddCategoryMapping(21, TorznabCatType.PCMac, "Apps/Mac");

            AddCategoryMapping(22, TorznabCatType.AudioMP3, "Music/MP3");
            AddCategoryMapping(23, TorznabCatType.AudioLossless, "Music/FLAC");
            AddCategoryMapping(24, TorznabCatType.Audio, "Music/MTV");

            AddCategoryMapping(25, TorznabCatType.PCGames, "Games/PC");
            AddCategoryMapping(26, TorznabCatType.Console, "Games/NSW");
            AddCategoryMapping(27, TorznabCatType.PCMac, "Games/Mac");
            
            AddCategoryMapping(28, TorznabCatType.Books, "Ebooks");

            AddCategoryMapping(30, TorznabCatType.XXX, "XXX/SD");
            AddCategoryMapping(31, TorznabCatType.XXX, "XXX/HD");
            AddCategoryMapping(32, TorznabCatType.XXX, "XXX/4K");

        }

        public override async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
        {
            LoadValuesFromJson(configJson);

            // TODO: implement captcha
            CookieHeader = configData.Cookie.Value;
            try
            {
                var results = await PerformQuery(new TorznabQuery());
                if (results.Count() == 0)
                {
                    throw new Exception("Your cookie did not work");
                }

                IsConfigured = true;
                SaveConfig();
                return IndexerConfigurationStatus.Completed;
            }
            catch (Exception e)
            {
                IsConfigured = false;
                throw new Exception("Your cookie did not work: " + e.Message);
            }
        }

        protected override async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            List<ReleaseInfo> releases = new List<ReleaseInfo>();
            var queryCollection = new NameValueCollection();
            var searchString = query.GetQueryString();
            var searchUrl = SearchUrl;

            queryCollection.Add("extendedSearch", "false");
            queryCollection.Add("freeleech", "false");
            queryCollection.Add("index", "0");
            queryCollection.Add("limit", "100");
            queryCollection.Add("order", "desc");
            queryCollection.Add("page", "search");
            if (query.ImdbID != null)
                queryCollection.Add("searchText", query.ImdbID);
            else
                queryCollection.Add("searchText", searchString);
            queryCollection.Add("sort", "d");
            queryCollection.Add("section", "all");
            queryCollection.Add("stereoscopic", "false");
            queryCollection.Add("watchview", "false");

            searchUrl += "?" + queryCollection.GetQueryString();
            foreach (var cat in MapTorznabCapsToTrackers(query))
                searchUrl += "&categories[]=" + cat;
            var results = await RequestStringWithCookies(searchUrl, null, SiteLink);

            try
            {
                //var json = JArray.Parse(results.Content);
                dynamic json = JsonConvert.DeserializeObject<dynamic>(results.Content);
                foreach (var row in json ?? System.Linq.Enumerable.Empty<dynamic>())
                {
                    var release = new ReleaseInfo();
                    var descriptions = new List<string>();
                    var tags = new List<string>();

                    release.MinimumRatio = 1.1;
                    release.MinimumSeedTime = 48 * 60 * 60;
                    release.Title = row.name;
                    release.Category = MapTrackerCatToNewznab(row.category.ToString());
                    release.Size = row.size;
                    release.Seeders = row.seeders;
                    release.Peers = row.leechers + release.Seeders;
                    release.PublishDate = DateTime.ParseExact(row.added.ToString() + " +01:00", "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
                    release.Files = row.numfiles;
                    release.Grabs = row.times_completed;

                    release.Comments = new Uri(SiteLink + "torrent/" + row.id.ToString() + "/");
                    release.Guid = release.Comments;
                    release.Link = new Uri(SiteLink + "api/v1/torrents/download/" + row.id.ToString());

                    if (row.frileech == 1)
                        release.DownloadVolumeFactor = 0;
                    else
                        release.DownloadVolumeFactor = 1;
                    release.UploadVolumeFactor = 1;


                    if (!string.IsNullOrWhiteSpace(row.firstpic.ToString()))
                    {
                        release.BannerUrl = (row.firstpic);
                    }


                    if (row.imdbid2 != null && row.imdbid2.ToString().StartsWith("tt"))
                    {
                        release.Imdb = ParseUtil.CoerceLong(row.imdbid2.ToString().Substring(2));
                        descriptions.Add("Title: " + row.title);
                        descriptions.Add("Year: " + row.year);
                        descriptions.Add("Genres: " + row.genres);
                        descriptions.Add("Tagline: " + row.tagline);
                        descriptions.Add("Cast: " + row.cast);
                        descriptions.Add("Rating: " + row.rating);
                        //descriptions.Add("Plot: " + row.plot);

                        release.BannerUrl = new Uri(SiteLink + "img/imdb/" + row.imdbid2 + ".jpg");
                    }

                    if ((int)row.p2p == 1)
                        tags.Add("P2P");
                    if ((int)row.pack == 1)
                        tags.Add("Pack");
                    if ((int)row.reqid != 0)
                        tags.Add("Request");

                    if (tags.Count > 0)
                        descriptions.Add("Tags: " + string.Join(", ", tags));

                    var preDate = row.preDate.ToString();
                    if (!string.IsNullOrWhiteSpace(preDate) && preDate != "1970-01-01 01:00:00")
                    {
                        descriptions.Add("Pre: " + preDate);
                    }
                    descriptions.Add("Section: " + row.section);

                    release.Description = string.Join("<br>\n", descriptions);

                    releases.Add(release);                    

                }
            }
            catch (Exception ex)
            {
                OnParseError(results.Content, ex);
            }

            return releases;
        }
    }
}
