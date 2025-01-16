﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Main.Core.Entities.SubEntities;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Quartz;
using WB.Core.BoundedContexts.Headquarters.DataExport.Factories;
using WB.Core.BoundedContexts.Headquarters.Services;
using WB.Core.BoundedContexts.Headquarters.Users.UserPreloading.Dto;
using WB.Core.BoundedContexts.Headquarters.Users.UserPreloading.Services;
using WB.Core.BoundedContexts.Headquarters.Users.UserPreloading.Tasks;
using WB.Core.BoundedContexts.Headquarters.Views.User;
using WB.Core.Infrastructure.PlainStorage;
using WB.Tests.Abc;

namespace WB.Tests.Unit.BoundedContexts.Headquarters
{
    [TestFixture]
    internal class UserImportServiceTests
    {
        [Test]
        public void When_verification_errors_more_than_max_allowed_errors_Then_UserPreloadingException_should_be_thrown()
        {
            //arrange
            var usersToImport = new UserToImport[10001];
            for (int i = 0; i <= 10000; i++)
                usersToImport[i] = Create.Entity.UserToImport("");

            var userImportService = CreateUserImportService(null, usersToImport);

            //act
            var exception = Assert.Catch<PreloadingException>(() => userImportService
                .VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray());

            //assert
            Assert.That(exception, Is.Not.Null);
        }

        [Test]
        public void When_missing_required_columns_Then_UserPreloadingException_should_be_thrown()
        {
            //arrange
            var csvReader = Mock.Of<ICsvReader>(x => x.ReadHeader(It.IsAny<Stream>(), It.IsAny<string>()) == new string[0]);

            var userImportService = Create.Service.UserImportService(csvReader: csvReader);

            //act
            var exception = Assert.Catch<PreloadingException>(() => userImportService
                .VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray());

            //assert
            Assert.That(exception, Is.Not.Null);
        }

        [Test]
        public void GetAvaliableDataColumnNames_Then_list_of_expected_columns_is_returned()
        {
            var userImportService = CreateUserImportService(null);

            var columnList = userImportService.GetUserProperties();

            Assert.That(columnList,
                Is.EqualTo(new[] { "login", "password", "role", "supervisor", "fullname", "email", "phonenumber", "workspace" }));
        }

        [Test]
        public void When_login_is_taken_by_existing_user_Then_record_verification_error_with_code_PLU0001()
        {
            //arrange
            var userName = "nastya";

            var userImportService = CreateUserImportService(
                new[]
                {
                    Create.Entity.HqUser(userName: userName)
                },
                Create.Entity.UserToImport(userName));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0001", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Login", errors[0].ColumnName);
            ClassicAssert.AreEqual(userName, errors[0].CellValue);
        }

        [Test]
        public void When_put_missing_workspace_Then_need_show_verification_error_with_code_PLU0022()
        {
            //arrange
            var workspace = "missing";
            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(workspace: workspace));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(
                new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0022", errors[0].Code);
            ClassicAssert.AreEqual(workspace, errors[0].CellValue);
        }
        
        [Test]
        public void When_2_users_with_the_same_login_are_present_in_the_dataset_Then_record_verification_error_with_code_PLU0002()
        {
            //arrange
            var userName = "nastya";

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(userName), Create.Entity.UserToImport(userName));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(
                new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(2, errors.Length);
            ClassicAssert.AreEqual("PLU0002", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Login", errors[0].ColumnName);
            ClassicAssert.AreEqual(userName, errors[0].CellValue);

            ClassicAssert.AreEqual("PLU0002", errors[1].Code);
            ClassicAssert.AreEqual(3, errors[1].RowNumber);
            ClassicAssert.AreEqual("Login", errors[1].ColumnName);
            ClassicAssert.AreEqual(userName, errors[1].CellValue);
        }

        [Test]
        public void When_interviewer_preloaded_with_supervisor_from_another_workspace_Then_record_verification_error_with_code_PLU0010()
        {
            //arrange
            var userName = "nastya";
            var supervisor = "arena";
            
            var userImportService = CreateUserImportService(new[]
                {
                    Create.Entity.HqUser(userName: supervisor, role: UserRoles.Supervisor, workspaces: new [] {"quake"})
                }, Create.Entity.UserToImport(userName, supervisor: supervisor, role: "Interviewer")
            );

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(
                new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0010", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Supervisor", errors[0].ColumnName);
            ClassicAssert.AreEqual(supervisor, errors[0].CellValue);
        }

        [Test]
        public void When_login_is_taken_by_archived_interviewer_in_other_team_Then_record_verification_error_with_code_PLU0003()
        {
            //arrange
            var userName = "nastya";
            var supervisorName = "super";

            var userImportService = CreateUserImportService(
                new[]
                {
                    Create.Entity.HqUser(userName: userName, supervisorId: Guid.NewGuid(), isArchived: true),
                    Create.Entity.HqUser(userName: supervisorName, role: UserRoles.Supervisor, workspaces: new []{ "space" })
                },
                Create.Entity.UserToImport(login: userName, supervisor: supervisorName));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0003", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Login", errors[0].ColumnName);
            ClassicAssert.AreEqual(userName, errors[0].CellValue);
        }

        [Test]
        public void When_login_is_taken_by_user_in_other_role_Then_record_verification_error_with_code_PLU0004()
        {
            //arrange
            var userName = "nastya";

            var userImportService = CreateUserImportService(
                new[]
                {
                    Create.Entity.HqUser(userName: userName, isArchived: true)
                },
                Create.Entity.UserToImport(userName, role: "supervisor"));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0004", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Login", errors[0].ColumnName);
            ClassicAssert.AreEqual(userName, errors[0].CellValue);
        }

        [Test]
        public void When_users_login_contains_invalid_characted_Then_record_verification_error_with_code_PLU0005()
        {
            //arrange
            var userName = "na$tya";

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(userName));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0005", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Login", errors[0].ColumnName);
            ClassicAssert.AreEqual(userName, errors[0].CellValue);
        }

        [TestCase("", "PLU0021")] //empty
        [TestCase("Q12wzyt#", "PLU0015")]
        [TestCase("Qwerty12345", "PLU0016")]
        [TestCase("QwertyQW$werty", "PLU0017")]
        [TestCase("QWE1TYQWWE$RTY", "PLU0018")]
        [TestCase("qw1erty$qwerty", "PLU0019")]
        [TestCase("qq1q$qqqqqqqqQ", "PLU0020")]
        public void When_users_password_is_empty_Then_record_verification_error_with_code_PLU0006(string password, string expectedCode)
        {
            //arrange
            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(password: password));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            Assert.That(errors, Has.Length.EqualTo(1));
            Assert.That(errors[0].Code, Is.EqualTo(expectedCode));
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Password", errors[0].ColumnName);
            ClassicAssert.AreEqual(password, errors[0].CellValue);
        }

        [Test]
        public void When_users_email_contains_invalid_characted_Then_record_verification_error_with_code_PLU0007()
        {
            //arrange
            var email = "na$tya";

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(email: email));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0007", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Email", errors[0].ColumnName);
            ClassicAssert.AreEqual(email, errors[0].CellValue);
        }

        [Test]
        public void When_users_phone_number_contains_invalid_characted_Then_record_verification_error_with_code_PLU0008()
        {
            //arrange
            var phoneNumber = "na$tya";

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(phoneNumber: phoneNumber));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0008", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("PhoneNumber", errors[0].ColumnName);
            ClassicAssert.AreEqual(phoneNumber, errors[0].CellValue);
        }

        [Test]
        public void When_users_role_is_undefined_Then_record_verification_error_with_code_PLU0009()
        {
            //arrange
            var undefinedRole = "undefined role";

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(role: undefinedRole));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0009", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Role", errors[0].ColumnName);
            ClassicAssert.AreEqual(undefinedRole, errors[0].CellValue);
        }

        [Test]
        public void When_user_in_role_interviewer_and_supervisor_not_found_Then_record_verification_error_with_code_PLU0010()
        {
            //arrange
            var interviewerName = "int";
            var supervisorName = "super";

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(login: interviewerName, supervisor: supervisorName, role: "interviewer"));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0010", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Supervisor", errors[0].ColumnName);
            ClassicAssert.AreEqual(supervisorName, errors[0].CellValue);
        }

        [Test]
        public void When_user_in_role_interviewer_and_supervisor_is_empty_Then_record_verification_error_with_code_PLU0010()
        {
            //arrange
            var interviewerName = "int";

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(login: interviewerName, role: "interviewer"));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0010", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Supervisor", errors[0].ColumnName);
            ClassicAssert.AreEqual("", errors[0].CellValue);
        }

        [Test]
        public void When_user_in_role_supervisor_has_not_empty_supervisor_column_Then_record_verification_error_with_code_PLU0011()
        {
            //arrange
            var supervisorName = "super";
            var supervisorCellValue = "super_test";

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(login: supervisorName, supervisor: supervisorCellValue, role: "supervisor"));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0011", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("Supervisor", errors[0].ColumnName);
            ClassicAssert.AreEqual(supervisorCellValue, errors[0].CellValue);
        }

        [Test]
        public void when_person_full_name_has_more_than_allowed_length_Should_return_error()
        {
            //arrange
            var fullName = new string('a', 101);

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(fullName: fullName));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0012", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("FullName", errors[0].ColumnName);
            ClassicAssert.AreEqual(fullName, errors[0].CellValue);
        }

        [Test]
        public void when_phone_number_more_than_allowed_length_Should_return_error()
        {
            //arrange
            var phone = new string('1', 16);

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(phoneNumber: phone));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0013", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("PhoneNumber", errors[0].ColumnName);
            ClassicAssert.AreEqual(phone, errors[0].CellValue);
        }

        [Test]
        public void when_person_full_name_has_illigal_characters_Should_return_error()
        {
            //arrange
            var fullName = "Имя 123";

            var userImportService = CreateUserImportService(null,
                Create.Entity.UserToImport(fullName: fullName));

            //act
            var errors = userImportService.VerifyAndSaveIfNoErrors(new MemoryStream(new byte[0]), "file.txt", "space").ToArray();

            //assert
            ClassicAssert.AreEqual(1, errors.Length);
            ClassicAssert.AreEqual("PLU0014", errors[0].Code);
            ClassicAssert.AreEqual(2, errors[0].RowNumber);
            ClassicAssert.AreEqual("FullName", errors[0].ColumnName);
            ClassicAssert.AreEqual(fullName, errors[0].CellValue);
        }

        [Test]
        public void when_uploaded_file_contains_quot()
        {
            string data = @"login	password	email	fullname	phonenumber	role	supervisor	workspace
            LmdYkeTihXA	P@$$w0rdless	mytest@email.com	bPVEbCTaOiR""jZNdZgAAHUMcGOVNBFI	112233	supervisor	space";

            var service = Create.Service.UserImportService(csvReader: new CsvReader(),
                authorizedUser: Mock.Of<IAuthorizedUser>(u => u.Workspaces == new[] { "space" }));

            // Act
            TestDelegate act = () => service.VerifyAndSaveIfNoErrors(new MemoryStream(Encoding.UTF8.GetBytes(data)), "file.txt", "space").ToList();

            // Assert
            Assert.DoesNotThrow(act);
        }

        private UserImportService CreateUserImportService(HqUser[] dbUsers = null, params UserToImport[] usersToImport)
        {
            var workspaces = new List<string>() {"space"};
            IAuthorizedUser user = Mock.Of<IAuthorizedUser>(u => 
                u.IsAdministrator == false && u.Workspaces == workspaces);
            return this.CreateUserImportServiceWithRepositories(dbUsers: dbUsers, usersToImport: usersToImport,
                authorizedUser: user);
        }

        private UserImportService CreateUserImportServiceWithRepositories(
            IPlainStorageAccessor<UsersImportProcess> importUsersProcessRepository = null,
            IPlainStorageAccessor<UserToImport> importUsersRepository = null,
            IAuthorizedUser authorizedUser = null,
            HqUser[] dbUsers = null,
            params UserToImport[] usersToImport)
        {
            var csvReader = Create.Service.CsvReader(new[]
            {
                nameof(UserToImport.Login), nameof(UserToImport.Password),
                nameof(UserToImport.Role), nameof(UserToImport.Supervisor),
                nameof(UserToImport.FullName), nameof(UserToImport.Email),
                nameof(UserToImport.PhoneNumber)
            }.Select(x => x.ToLower()).ToArray(), usersToImport);

            var userStorage = Create.Storage.UserRepository(dbUsers ?? new HqUser[0]);

            return Create.Service.UserImportService(
                csvReader: csvReader,
                userStorage: userStorage,
                authorizedUser: authorizedUser);
        }
    }
}
