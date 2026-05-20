namespace VoiceConcierge.Api.Data;

public static class MeridianSeed
{
    public sealed record Faq(string Question, string Answer, string[] Tags);

    public static readonly Faq[] Faqs =
    [
        new("What are the casino hours?", "Our casino is open 24 hours a day, 7 days a week.", ["general", "hours"]),
        new("What time is hotel check-in and check-out?", "Hotel check-in is at 4:00 PM (early check-in subject to availability) and check-out is at 11:00 AM, with late checkout available for suite guests.", ["general", "hotel"]),
        new("How much is parking?", "Self parking is free for all guests. Valet parking is complimentary for hotel guests and $25 for visitors.", ["general", "parking"]),
        new("What is the dress code?", "Smart casual throughout the property; formal attire is required at Aurelia restaurant and Eclipse Lounge.", ["general", "dress code"]),
        new("What is the age requirement?", "Guests must be 21 or older for the casino floor and bars. All ages are welcome in the hotel and family restaurants.", ["general", "age"]),
        new("Is Wi-Fi available?", "Complimentary Wi-Fi is available throughout the property, with premium high-speed access in suites.", ["general", "wifi"]),
        new("Is the poker room open?", "Yes, our poker room operates 24/7 with Texas Hold'em (No Limit and Limit), Omaha, and Seven Card Stud. Daily tournaments run at 11 AM and 7 PM with a $200 buy-in.", ["gaming", "poker"]),
        new("What slot machines do you have?", "We have over 2,000 machines from $0.01 to $1,000 per spin, including video slots, classic reels, and progressive jackpots. The largest jackpot is currently $4.2 million.", ["gaming", "slots"]),
        new("What blackjack tables are available?", "We offer 40 blackjack tables with minimums from $25 to $10,000, including single deck, 6-deck, and Spanish 21.", ["gaming", "blackjack"]),
        new("Do you have a sports book?", "Yes, our sports book is an 80-seat theater with a 40-foot screen and full bar service. Mobile betting is available property-wide via the Meridian app.", ["gaming", "sportsbook"]),
        new("Tell me about the high limit salon.", "The High Limit Salon is a private gaming area with dedicated hosts, available by invitation or a $50,000 credit line. It includes private restrooms, complimentary dining, and personal butler service.", ["gaming", "highlimit"]),
        new("What room types do you offer?", "We offer Deluxe Rooms (from $299/night), Premier Rooms (from $449), Luxury Suites (from $799), Penthouse Suites (from $2,500), and the Chairman's Villa (by inquiry).", ["rooms"]),
        new("Do you have accessible rooms?", "Yes, accessible rooms are available in all categories with roll-in showers, lowered amenities, and visual alerts. Please request at booking.", ["rooms", "accessibility"]),
        new("What is your best restaurant?", "Our signature restaurant is Aurelia, modern French cuisine by Chef Marcus Webb with two Michelin stars. Dinner only, 6 to 10 PM, reservations required, formal attire.", ["dining"]),
        new("What dining options are there?", "Aurelia (fine French), Silk Road (Pan-Asian), The Steakhouse (American), Café Meridian (all-day casual), and the Pool Bar & Grill (poolside, hotel guests only).", ["dining"]),
        new("What bars and lounges are there?", "Eclipse Lounge (rooftop, Strip views, 5 PM-2 AM), The Vault (whiskey and cigar lounge, 400+ whiskeys, 21+), and six casino floor bars with complimentary drinks for active players.", ["bars"]),
        new("Tell me about the spa.", "The Meridian Spa is a full-service spa and salon offering massage, facials, and body treatments, with couples suites. Open 8 AM to 8 PM; booking 24 hours in advance is recommended.", ["amenities", "spa"]),
        new("What entertainment is available?", "The Meridian Theater is a 1,200-seat venue with acclaimed residencies (tickets from $95), and NOVA nightclub is open Friday and Saturday 10:30 PM-4 AM.", ["amenities", "entertainment"]),
        new("Do you host weddings or events?", "Yes, we host weddings (packages $5,000-$150,000, coordinator included) and private events in rooms and ballrooms from 500 to 15,000 sq ft with full catering and AV.", ["events"]),
        new("Are there celebration packages?", "Celebration packages for birthdays and anniversaries start at $500 and include a room upgrade, champagne, custom cake, and dinner credit (72-hour notice required).", ["events"]),
        new("Are there partner discounts nearby?", "With your room key: Carbone (15% off food, priority reservations), Omega Mart ($10 off), Vegas Nights helicopter tours (20% off), Top Golf (free hour with 2-hour booking), and more.", ["partners"]),
    ];

    public sealed record SeedVoice(int Id, string Name, string Description, string ProviderVoiceId);

    public static readonly SeedVoice[] Voices =
    [
        new(1, "James", "Male, mature, warm British accent. Professional and refined.", "en-GB-RyanNeural"),
        new(2, "Sofia", "Female, friendly, subtle European accent. Welcoming and elegant.", "en-IE-EmilyNeural"),
        new(3, "Marcus", "Male, American, confident and energetic. Modern and approachable.", "en-US-GuyNeural"),
        new(4, "Elena", "Female, American, calm and reassuring. Sophisticated and clear.", "en-US-AriaNeural"),
    ];

    public const string SampleText =
        "Welcome to The Meridian Casino and Resort. How may I assist you this evening?";

    public const int DefaultActiveVoiceId = 1;
}
