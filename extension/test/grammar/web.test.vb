' SYNTAX TEST "source.asp.vb.net" "HTML, JavaScript, CSS, XML and JSON in VB strings (ADR 0011)"
Partial Class Admin
 Sub Load()
  tHtmlOut = "<td class=""Title"" style=""width: 10px; color: #FFF;"" onclick=""Go('a', 3);"">&nbsp;" +
' ^^^^^^^^ meta.html-statement.vb variable.other.asp
'              ^^ meta.html-fragment.vb meta.tag.html entity.name.tag.html
'                 ^^^^^ entity.other.attribute-name.html
'                       ^^^^^^^^^ string.quoted.double.html
'                                         ^^^^^ meta.attribute.style.html support.type.property-name.css
'                                                ^^ constant.numeric.css
'                                                  ^^ keyword.other.unit.css
'                                                             ^^^^ constant.other.color.rgb-value.hex.css
'                                                                     ^^^^^^^ meta.attribute.event-handler.html entity.other.attribute-name.html
'                                                                               ^^ entity.name.function.js
'                                                                                  ^^^ string.quoted.single.js
'                                                                                       ^ constant.numeric.decimal.js
'                                                                                            ^ punctuation.definition.tag.end.html
'                                                                                             ^^^^^^ constant.character.entity.html
'                                                                                                    ^^ meta.html-statement.vb - string
             IIf(Active, "<b>", "") + dr("Name").ToString() + "</td><!-- row -->"
'                         ^ meta.round-brackets string.quoted.double.asp meta.tag.html punctuation.definition.tag.begin.html
'                                         ^^^^ meta.html-fragment.vb - entity
'                                                                ^^ entity.name.tag.html
'                                                                   ^^^^^^^^^^^^ comment.block.html
  tHtml = "<td class=" + Q + "Name" + Q + ">" + x + "</td>"
'                             ^^^^ meta.html-fragment.vb - meta.tag
'                                          ^ meta.tag.html punctuation.definition.tag.end.html
  OutHtml = " class=""a"" title='b'>"
'             ^^^^^ meta.tag.html entity.other.attribute-name.html
'                               ^^^ string.quoted.single.html
'                                  ^ punctuation.definition.tag.end.html
  PageTitle.InnerHtml = "Members <span class=""Errors"">*</span>"
' ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ meta.html-statement.vb
'                                 ^^^^ entity.name.tag.html
  tMsg = "Saved!<br />"
'                ^^ - entity.name.tag.html
  Body.Attributes.Add("onload", "ShowMsg(); return false;")
' ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ meta.javascript-statement.vb
'                      ^^^^^^ string.quoted.double.asp - meta.javascript-fragment.vb
'                                ^^^^^^^ meta.javascript-fragment.vb entity.name.function.js
'                                           ^^^^^^ keyword.control.js
'                                                  ^^^^^ constant.language.boolean.js
  ShowMsg.Attributes("onclick") = "if (confirm('Sure?')) this.form.submit();"
'                     ^^^^^^^ - meta.javascript-fragment.vb
'                                  ^^ keyword.control.js
'                                      ^^^^^^^ entity.name.function.js
'                                              ^^^^^^^ string.quoted.single.js
'                                                        ^^^^ variable.language.this.js
  SBMenu.AddItem("L", "CallEdit(" + KeyID.Value.ToString() + ",'F.aspx');", "New Entry")
'                 ^ string.quoted.double.asp - meta.javascript-fragment.vb
'                      ^^^^^^^^ entity.name.function.js
'                                                              ^^^^^^^^ string.quoted.single.js
'                                                                            ^^^^^^^^^ - meta.javascript-fragment.vb
  SBMenu.AddItem("P", "/Admin/Page.aspx", "Pages")
' ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ - meta.javascript-statement.vb
  Row.Attributes("style") = "display: none; background-color: #eeeeee;"
' ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ meta.css-statement.vb
'                 ^^^^^ - meta.css-fragment.vb
'                            ^^^^^^^ support.type.property-name.css
'                                     ^^^^ support.constant.property-value.css
'                                           ^^^^^^^^^^^^^^^^ support.type.property-name.css
  Row.Attributes.Add("style", "cursor: pointer;")
'                              ^^^^^^ meta.css-statement.vb support.type.property-name.css
  ' lang=js
' ^^^^^^^^^ meta.javascript-statement.vb comment.line.apostrophe.asp
'   ^^^^^^^ keyword.other.language-marker.vb
  strScript = "var aMsg=""" + aMsg + """; function ShowMsg() { if (aMsg.length>0) { alert(aMsg); } }"
'              ^^^ storage.type.js
'                       ^^ constant.character.escape.apostrophe.asp
'                                         ^^^^^^^^ storage.type.js
'                                                  ^^^^^^^ entity.name.function.js
  ' language = XML
  OutParms = "<?xml version=""1.0""?>" +
'             ^^ meta.xml-statement.vb meta.tag.preprocessor.xml punctuation.definition.tag.xml
'               ^^^ entity.name.tag.xml
'                   ^^^^^^^ entity.other.attribute-name.localname.xml
'                           ^^^^^^^ string.quoted.double.xml
             "<methodCall><methodName>Console.Command</methodName>" +
'              ^^^^^^^^^^ entity.name.tag.localname.xml
             "<x:value xmlns:x=""urn:x""/><![CDATA[a<b]]></methodCall>"
'              ^ entity.name.tag.namespace.xml
'                ^^^^^ entity.name.tag.localname.xml
'                      ^^^^^ entity.other.attribute-name.namespace.xml
'                                         ^^^^^^^^^^^^^^^ string.unquoted.cdata.xml
  ' lang=json
  tOut = "{""Error"":""Missing Site"",""Count"":3,""Ok"":true}"
'         ^ meta.json-statement.vb punctuation.definition.dictionary.json
'          ^^^^^^^^^ support.type.property-name.json
'                   ^ punctuation.separator.dictionary.key-value.json
'                    ^^^^^^^^^^^^^^^^ string.quoted.double.json
'                                               ^ constant.numeric.json
'                                                        ^^^^ constant.language.json
  ' lang=html
  SQLCmd = "<b>"
'            ^ meta.html-statement.vb entity.name.tag.html - meta.sql-statement.vb
  ' lang=sql
  tHtml = "Select Name From users"
'          ^^^^^^ meta.sql-statement.vb keyword.other.sql - meta.html-statement.vb
  ' lang=css

  tAfterBlank = "<b>"
' ^^^^^^^^^^^^^^^^^^^ - meta.css-statement.vb
  x = 1
' ^^^^^ - meta.html-statement.vb meta.javascript-statement.vb meta.css-statement.vb
 End Sub
tHtml = "<b>" + SQLCmd
'^^^^ meta.html-statement.vb variable.other.asp
'         ^ entity.name.tag.html
'               ^^^^^^ meta.html-statement.vb - meta.sql-statement.vb
SQLCmd = "Select 1"
'         ^^^^^^ meta.sql-statement.vb keyword.other.sql
End Class
