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
        const string PATH_WITH_REPOSITORIES = @"E:\repositories\top10repos";

        // This string should contain where you want the CSV output
        const string CSV_OUTPUT_PATH = @"E:\repositories\mre_results.csv";

        // This queue stores the names of the repositories for the threads to pick their next work from
        static ConcurrentQueue<string> repoPaths = new ConcurrentQueue<string>();

        // Stores the repository name and mean value for the repository
        static ConcurrentDictionary<string, long> repoMeanValues = new ConcurrentDictionary<string, long>();

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
            using (StreamWriter file = new System.IO.StreamWriter(outputFile))
            {
                file.WriteLine("repo,mean_value");  // CSV-format header
                foreach (var repoPair in repoMeanValues) {
                    file.WriteLine(String.Format("{0},{1}", repoPair.Key, repoPair.Value)); // CSV-entries
                }
                file.Flush();
                file.Close();
            }
        }

        private static long CalculateMeanExperienceForRepository(string pathToSpecificRepo)
        {
            // Open the repository using the LibGit2Sharp library
            using (var repo = new Repository(pathToSpecificRepo))
            {
                // Collect all author names into a set
                HashSet<string> uniqueAuthors = new HashSet<string>();
                foreach (Commit c in repo.Commits)
                {
                    uniqueAuthors.Add(c.Author.Name.ToString());
                }

                // Values to store experience for each author and calculate a mean sum for the repository
                long totalDays = 0;
                long totalAuthorsConsidered = 0;

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
                        totalDays += daysSinceFirstCommit;
                        totalAuthorsConsidered++;
                    }
                }

                long meanValue = 0;
                if (totalAuthorsConsidered > 0) // Prevents division by zero if all authors were ineligible
                {
                    meanValue = totalDays / totalAuthorsConsidered;
                }
                
                return meanValue;
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
                Thread.Sleep(0);
                long meanValue = CalculateMeanExperienceForRepository(repositoryPath);
                Console.WriteLine(string.Format("Thread {0}: {1}, mean: {2}", Thread.CurrentThread.ManagedThreadId, repositoryName, meanValue));
                repoMeanValues.TryAdd(repositoryName, meanValue);
            }
        }

        static void Main(string[] args)
        {
            EvaluateRepositories(CSV_OUTPUT_PATH);
        }
    }
}
