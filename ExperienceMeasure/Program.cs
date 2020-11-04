using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LibGit2Sharp;

namespace ExperienceMeasureCSharp
{
    class Program
    {
        // This string should contain the path to the repositories you would like to analyse
        const string PATH_WITH_REPOSITORIES = @"E:\repositories\first500repos";

        // This string should contain where you want the CSV output
        const string CSV_OUTPUT_PATH = @"E:\repositories\mre_results.csv";

        // This queue stores the names of the repositories for the threads to pick their next work from
        static ConcurrentQueue<string> repoPaths = new ConcurrentQueue<string>();

        // Stores the repository data for CSV output
        static SynchronizedCollection<RepositoryData> repositoryDatas = new SynchronizedCollection<RepositoryData>();

        // A filter for LibGit2Sharp to reverse the list of commits when looking for the first commit by a contributor
        static CommitFilter reverseFilter = new CommitFilter()
        {
            SortBy = CommitSortStrategies.Reverse
        };

        private static void EvaluateRepositories(string outputFile) {

            // Read all repositories in to a queue
            foreach (string directory in Directory.GetDirectories(PATH_WITH_REPOSITORIES)) {
                repoPaths.Enqueue(directory);
            }
            
            // Create a suitable number of threads for concurrent work, but not to hog all resources unless necessary
            int processorCount = Environment.ProcessorCount > 1 ? Environment.ProcessorCount - 2 : Environment.ProcessorCount;
            List<Thread> workerThreads = new List<Thread>();

            // Create and start threads
            for (int i = 0; i < processorCount; i++) {
                Thread thread = new Thread(new ThreadStart(ThreadProcess));
                thread.Start();
                workerThreads.Add(thread);
            }

            // Join the threads when they are finished
            foreach (Thread thread in workerThreads) {
                thread.Join();
            }

            // Write results to file
            // Order: 
            //  repository name,
            //  number of authors considered,
            //  smallest value
            //  middle value (number of authors/2)
            //  largest value
            //  mean (int division)
            using (StreamWriter file = new System.IO.StreamWriter(outputFile))
            {
                file.WriteLine("repo,authors,smallest,middle,largest,mean");  // CSV-format header
                foreach (var repoData in repositoryDatas) {
                    file.WriteLine(repoData.toCSV()); // CSV-entries
                }
                file.Flush();
                file.Close();
            }
        }

        private static RepositoryData GetRepositoryExperienceData(string repositoryName, string pathToSpecificRepo)
        {
            // Open the repository using the LibGit2Sharp library
            using (var repo = new Repository(pathToSpecificRepo))
            {
                RepositoryData repositoryData = new RepositoryData(repositoryName);

                // Collect all author names into a temporary set, these are not stored
                HashSet<string> uniqueAuthors = new HashSet<string>();
                foreach (Commit c in repo.Commits)
                {
                    uniqueAuthors.Add(c.Author.Name.ToString());
                }

                // Get first and last commit for each eligible author and add it up
                foreach (string author in uniqueAuthors)
                {
                    // Get the latest commit
                    Commit lastCommit = repo.Commits.Where(c => c.Author.Name == author).First();

                    // Now reverse the list of commits and take the first one by the same author
                    Commit firstCommit = repo.Commits.QueryBy(reverseFilter).Where(c => c.Author.Name == author).First();

                    // Calculate the days between the two commits
                    long daysSinceFirstCommit = (lastCommit.Author.When.ToUnixTimeSeconds() - firstCommit.Author.When.ToUnixTimeSeconds()) / 86400;

                    // The author is eligible for inclusion in the mean if there has been
                    // at least a day since their first commit
                    if (daysSinceFirstCommit >= 1)
                    {
                        repositoryData.addValueToList(daysSinceFirstCommit);
                    }
                }
                
                return repositoryData;
            }
        }

        // The task that all threads will be working with
        public static void ThreadProcess()
        {
            string repositoryPath;

            // While there is still work to be done, keep picking new paths from the shared queue
            while (repoPaths.TryDequeue(out repositoryPath))
            {
                string repositoryName = new DirectoryInfo(repositoryPath).Name;
                RepositoryData repositoryData = GetRepositoryExperienceData(repositoryName, repositoryPath);
                Thread.Sleep(0);    // Makes printing cleaner
                Console.WriteLine(string.Format("Thread {0}: {1}, mean: {2}", Thread.CurrentThread.ManagedThreadId, repositoryName, repositoryData.averageIntDivision()));
                repositoryDatas.Add(repositoryData);
            }
        }

        static void Main(string[] args)
        {
            EvaluateRepositories(CSV_OUTPUT_PATH);
        }
    }

    class RepositoryData {
        private string repositoryName;
        private List<long> daysOfExperienceWithinRepository;    // == number of counted contributors

        public RepositoryData(string repositoryName) {
            this.repositoryName = repositoryName;
            daysOfExperienceWithinRepository = new List<long>();
        }

        public void addValueToList(long value) {
            // A value of 0 is invalid
            if (value > 0) {
                daysOfExperienceWithinRepository.Add(value);
            }
        }

        long largestValue() {
            if (numberOfAuthorsConsidered() > 0) {  // Throws if list is empty
                return daysOfExperienceWithinRepository.Max();
            }
            return 0; // No authors considered
        }
           
        // Authors with at least two commits and a full day between them
        int numberOfAuthorsConsidered() {
            return daysOfExperienceWithinRepository.Count();
        }

        long smallestValue() {
            if (numberOfAuthorsConsidered() > 0){ // Throws if list is empty
                return daysOfExperienceWithinRepository.Min();
            }

            return 0; // No authors considered
        }

        public long averageIntDivision() {
            if (numberOfAuthorsConsidered() > 0) {    // Prevents division by zero
                return daysOfExperienceWithinRepository.Sum() / numberOfAuthorsConsidered();
            }
            return 0; // No authors considered
        }

        double averageWithDecimals()
        {
            if (numberOfAuthorsConsidered() > 0) {
                return daysOfExperienceWithinRepository.Average();
            }
            return 0; // No authors considered
        }

        long middleValue()
        {
            int consideredAuthors = numberOfAuthorsConsidered();
            if (consideredAuthors > 0) {    // Prevents division by zero
                daysOfExperienceWithinRepository.Sort();
                return daysOfExperienceWithinRepository.ElementAt(consideredAuthors/2);
            }
            return 0; // No authors considered
        }

        // Order: 
        //  repository name,
        //  number of authors considered,
        //  smallest value,
        //  middle value (number of authors/2)
        //  largest value,
        //  mean (int division)

        public string toCSV() {
            return string.Join(",", repositoryName, numberOfAuthorsConsidered(), 
                smallestValue(), middleValue(), largestValue(), averageIntDivision());
        }
    }
}
