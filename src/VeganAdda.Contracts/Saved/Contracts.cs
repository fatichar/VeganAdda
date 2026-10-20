namespace VeganAdda.Contracts;

public record SavedItems(List<Person> People, List<EventCard> Events, int PeopleTotal = 0, int EventsTotal = 0, int PeoplePage = 1, int EventsPage = 1);

public interface ISavedApi
{
    Task<SavedItems> GetSaved(int peoplePage = 1, int eventsPage = 1);
}
