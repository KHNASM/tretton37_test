# tretton37_test
Solution for interview test for tretton37


Sample Web Scrapper

# Application

This command-line application efficiently downloads website content based on a provided URL. It fetches the following resources:

-   **All web pages within the same domain:** This includes every navigable page accessible from the starting URL.
-   **Linked resources within the same domain:** This encompasses stylesheets, fonts, and other files linked within the HTML code that contribute to the website's appearance and functionality.
-   **Referenced scripts within the same domain:** This covers JavaScript files used by the website to add interactivity and dynamic behaviour.
-   **Embedded assets within the same domain:** This includes images and other media files directly referenced by the web pages, enhancing the visual experience.

The downloaded files replicate the original website's structure exactly. This means you can browse the downloaded content locally, experiencing the same look, feel, and navigation as the live website.

## Same-origin

The application focuses on downloading resources that originate from the same domain (website) as the starting URL. While it won't download resources from external domains, any links to these external resources are preserved within the downloaded files.

## Scope

With a development timeframe of 3-6 hours, the developer prioritized a technically well-rounded application. They aimed to showcase the most advanced features possible within this constraint, rather than simply demonstrating the full breadth of their skills.

# Development Tools

Tools mainly from the Microsoft technology stack were used to develop this demo application:

-   **IDE:** Microsoft Visual Studio 2022
-   **Programming Language:** C\# 12.0
-   **SDK:** Microsoft .Net 8.0 SDK
-   **Target Runtime:** Microsoft .Net 8.0 CLR or compatible runtime
-   **Libraries:**
    -   .Net 8.0 Base Class Library
        -   .Net 8.0 Task Parallel Library (TPL)
    -   HTML Agility Pack 1.11.61

# Design

## High Level Program Flow

The core component it WebScraper. Following is pseudocode (roughly) describes how it works:

1.  Read
    1.  Starting URL
    2.  Options
2.  Queue the Start URL in a Concurrent Queue CQ
3.  **WHILE** Queue has any items
    1.  Dequeue next URLs
    2.  Download the resource pointed to by this next URL
        1.  **IF** download timed out (e.g. restricted throttle/DDoS prevention), requeue the URL in the CQ and Goto ‎3
        2.  **ELSE** Goto ‎c
    3.  **IF** the resource is a web page
        1.  Scrape the embedded URLs
        2.  **For each** embedded URL
            1.  **IF** the URL is to a web page, Queue each URL again in the CQ
            2.  **ELSE** store the URL in a collection DC for later processing (‎4)
        3.  Goto ‎3
    4.  **ELSE** Goto ‎3
4.  **For each** URL in the collection DC (‎3.c.ii.2 )
    1.  Download the resource
        1.  **IF** download times out and retry is configured (e.g. restricted throttle/DDoS prevention), requeue the URL in the CQ
5.  **IF** the CQ again has items in it, Goto ‎3
6.  **ELSE** end.
7.  **(Cross Cutting: in case of error (other than time out) in processing a URL, the fact is logged and process proceeds with next URL rather than aborting the entire process)**

## Parallelism

Batch based parallelism is used, i.e. URLs in a batch of one or more (up to a given max number – defaulting the Processor Count) are processes in parallel using the TPL. This works as follows:

At step ‎3.a of the pseudocode in the section ‎3.1, URLs up to a configured max number or the current CQ count (whichever is less) are dequeued and handed to threads that run in parallel.

Hence the size of batch remains non-deterministic.

## Output Logs

Comprehensive output logs are omitted. To indicate progress. They are colour-coded/annotated to elaborate the process in the real time.

-   Green [Success] entries normally indicate a resource was downloaded successfully
-   Yellow [Warning] entries normally mean a problem in the HTML, e.g. a malformed embedded URL
-   Red [Error] entries normally indicate error
-   Cyan [Emphasis] indicate significant event, e.g. requeuing, starting a sub-process and so on
-   Dark Gray [Insignificant] entries normally used when duplicate URL are skipped.
-   Gray [Normal] to indicate normal messaging

This detailed and elaborate logging helps both the understanding of inner working as well as help diagnose bugs or malfunctioning.

## Loose Coupling

Concerns are isolated and decoupled. For example logging and file handling is decoupled from the core Scraper. Scrapper relies on abstraction meaning that different implementations can be injected. E.g. download filing destination can easily be replaced with a remote API based (e.g. amazon S3), and Console logging can be replaced with third part API based logging.

## Dependency Injection

Dependency injection is carried out at startup statically rather than using a DI container that can resolve dependencies on the run time. This is because a) A DI Container would be an overkill for this small application, 2) Would introduce further dependencies (e.g. NuGet package/libraries) and 3) since dependencies are statically known beforehand the runtime resolution would only harm the performance without any benefit.

## Code Comments

Code comments are avoided at most, as they can easily go out of sync. Instead elaborate naming and composition is used in such a smart way that the code speaks itself rather than relying on the comments to speak for it.

### Options and Configuration

The application takes a number of command line argument to specify the options and/or configuration. There is built in ‘help’ written, so that if the application gets started without any arguments, it guides the user by display ‘usage’ on the console. (See Appendix A).

# Potential Improvements

The developer acknowledges there might be ways to optimize the solution further. However, due to constraints like project scope, timeframe, and complexity, a balance was struck to achieve the best possible outcome within those limitations. Here are some additional thoughts on potential improvements.

1.  **Parallel Processing Consideration:** The developer explored the possibility of fully orchestrated parallelism, which could potentially enhance performance even further. However, this approach was deemed unnecessary due to two factors: (a) the limited development timeframe, and (b) it would be an excessive level of complexity for this application. The chosen method already achieved significant performance gains, reducing processing time by up to ten times. This method, leveraging the Task Parallel Library (TPL), avoided introducing additional locking mechanisms and relied on well-established synchronization techniques. This approach not only improved performance but also enhanced code maintainability and simplified debugging.
2.  **Mitigating Throttling and Anti-DDOS Measures:** To minimize the risk of encountering throttling or anti-DDS protection on the target website, the application implements the following techniques:
-   **Configurable Retry Logic:** A command-line argument allows you to enable retrying timed-out HTTP requests. This can help overcome temporary network issues or server overload.
-   **Adjustable Parallelism:** Another command-line argument lets you specify the number of parallel processes used for downloading. This defaults to the number of cores on your machine, but you can adjust it downwards if the server seems overwhelmed by the request rate.

    While these features offer some level of mitigation, there are limitations to consider:

-   **Timeframe Constraints:** Implementing a more sophisticated throttling mitigation strategy would require additional development time.
-   **Application Size:** A complex solution could increase the overall complexity of the application and would be an overkill for this level of application.

    In this context, the chosen approach provides a reasonable balance between effectiveness, simplicity, and development constraints.

1.  **Resource Type Identification:** The application currently relies solely on file extensions to identify resource types. While methods like MIME types, Content-Type headers, or even content analysis can provide more accurate identification, the developer opted for a simpler approach due to the following considerations:
-   **Application Size:** Implementing a more robust identification system would increase the application's complexity.
-   **Timeframe Constraints:** Developing and integrating a more complex approach would require additional development time.
1.  **Dynamically Generated Resources:** This solution might not function correctly with resources generated dynamically, especially URLs containing query parameters (e.g., "?sort=newest"). While the developer acknowledges this limitation, they chose not to implement mitigation for the following reasons:
-   **Timeframe Constraints (a):** Implementing support for dynamic resources would have extended development time beyond the allocated timeframe.
-   **Target Website Analysis (b):** An examination of the target website revealed that it doesn't currently utilize parameterized URLs.
-   **Focus on Static Content (c):** The primary goal was to download static website content. While a simple file storage system wouldn't handle dynamic requests, it's sufficient for the intended purpose.

    It's noteworthy that the developer demonstrates an understanding of handling dynamic requests. This is evident by their conversion of parameterized URLs within the Style.css file to static versions.

# Build and Deployment

## Visual Studio:

-   Build Your Application:
-   Open your .NET 8.0 console application project in Visual Studio 2022.
-   Build the project to ensure it compiles successfully.
-   Publish the Application:
-   Right-click on your project in the Solution Explorer.
-   Choose “Publish” from the context menu.
-   Configure the publish settings (target framework, runtime, etc.).
-   Click “Publish” to generate the deployment files.

### Choose Deployment Type:

-   You can select either “Framework-Dependent Deployment (FDD)” or “Self-Contained Deployment (SCD)” based on your requirements.

## .NET CLI:

-   Open a Terminal or Command Prompt:
-   Navigate to your project directory.
-   Publish the Application:
-   Run the following command for FDD:

   ``` dotnet publish -c Release -p:UseAppHost=false ```

-   Or for SCD (replace \<RID\> with the appropriate runtime identifier):

   ``` dotnet publish -c Release -r \<RID\> --self-contained true ```

### Find the Published Files:

-   The published files will be in the ./bin/Release/net8.0/publish/ directory (for FDD) or in a platform-specific folder (for SCD).
-   Remember to adjust the settings according to your specific needs.

## Distribution:

After the application has been published using Visual Studio 2022 or the .NET CLI, navigate to the publish folder (e.g., ./bin/Release/net8.0/publish/). Copy the contents and distribute them to the end users. The content will vary based on the deployment type selected during the publishing process, whether it is Self-Contained or Framework-Dependent.

Please note that framework-dependent publishing requires the targeted framework to be installed on the target machine.

# How to use

Open a terminal and navigate to the directory containing WebScrapper.exe. Execute the WebScrapper.exe file and follow the on-screen instructions. For example:

``` C:\WebScrapper\WebScrapper "https://www.example.com" "od:C:\Scrapper_Output" "pc:20" "htm:html,htm" ```

Refer to Appendix A for detailed information on required and optional arguments.

## Appendix A – Options/Configurations (Command Line Arguments)

![A screenshot of a computer program Description automatically generated](https://asre.dev/wp-content/uploads/2024/06/tretton37_scrapper_ss-1.jpg)
